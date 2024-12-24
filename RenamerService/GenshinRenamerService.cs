using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;

namespace RenamerService {


    /// <summary>
    ///   The background service to continuously monitor the "Screenshots" or "Game DVR" folder, 
    ///   and renames any screenshot file having file name pattern "dd_mm_yyyy" into "yyyy-mm-dd" pattern.
    /// </summary>
    public class GenshinRenamerService : BackgroundService {

        #region Fields

        /// <summary>
        ///   The logger that writes any log output to the "Applications\GenshinRenamerService" event logs. <br/>
        ///   These logs are visible through the Event Viewer system app.
        /// </summary>
        private readonly ILogger<GenshinRenamerService> mLogger;

        /// <summary>List of watchdogs that monitor any file addition or removal to the watched directories.</summary>
        private List<FileSystemWatcher> mFSWs;

        /// <summary>List of pending files to rename.</summary>
        private Queue<Tuple<string, string>> mPendingRenames;

        /// <summary>The regex to apply to screenshot files.</summary>
        private Regex mRegex;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new <see cref="GenshinRenamerService"/> service.
        /// </summary>
        /// <param name="logger">
        ///   The logger that writes any log output to the "Applications\GenshinRenamerService" event logs. <br/>
        ///   These logs are visible through the Event Viewer system app.
        /// </param>
        public GenshinRenamerService(ILogger<GenshinRenamerService> logger) {
            mLogger         = logger;
            mFSWs           = new List<FileSystemWatcher>(2);
            mRegex          = new Regex(@"^(?<fname>.*)(?<day>\d{2})_(?<month>\d{2})_(?<year>\d{4})\s(?<hour>\d{2})_(?<minute>\d{2})_(?<second>\d{2})(?<ext>.*)$");
            mPendingRenames = new Queue<Tuple<string, string>>(4);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Runs when the <see cref="IHostedService"/> starts. This task represents the lifetime of the long running operation(s) being performed.
        /// </summary>
        /// <param name="stoppingToken">A cancellation token that is triggered when <see cref="IHostedService.StopAsync(CancellationToken)"/> is called.</param>
        /// <returns>A <see cref="Task"/> that represents the long running operations.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            Task initialRenameTask;
            DisposeFSWs();
            ReadConfig();
            EnableFSWs();
            try { 
                if (!stoppingToken.IsCancellationRequested) { 
                    initialRenameTask = new Task(new Action<object?>(InitialRenameFilesAction), stoppingToken, stoppingToken);
                    initialRenameTask.Start();
                    await initialRenameTask;
                }
                while (!stoppingToken.IsCancellationRequested) {
                    await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
                    PeriodicRenameFilesAction();
                }
            }
            catch (OperationCanceledException) {
                //do nothing, let the service terminate
            }
            DisposeFSWs();
        }

        /// <summary>
        /// Disposes all existing <see cref="FileSystemWatcher"/> objects in [<see cref="mFSWs"/>].
        /// </summary>
        private void DisposeFSWs() {
            int i = 0;
            while (i < mFSWs.Count) {
                mFSWs[i].Dispose();
                ++i;
            }
            mFSWs.Clear();
        }

        /// <summary>
        /// Pauses all <see cref="FileSystemWatcher"/> objects in [<see cref="mFSWs"/>] from monitoring changes in their watched directories.
        /// </summary>
        private void DisableFSWs() {
            int i = 0;
            while (i < mFSWs.Count) {
                mFSWs[i].EnableRaisingEvents = false;
                ++i;
            }
        }

        /// <summary>
        /// Enables or unpauses all <see cref="FileSystemWatcher"/> objects in [<see cref="mFSWs"/>] to begin or to resume monitoring changes in their watched directories.
        /// </summary>
        private void EnableFSWs() {
            int i = 0;
            while (i < mFSWs.Count) {
                mFSWs[i].EnableRaisingEvents = true;
                ++i;
            }
        }

        /// <summary>
        /// Reads config file "Genshin renamer service settings.txt".
        /// </summary>
        private void ReadConfig() {
            string configPath;
            string? watchedFolder;
            StreamReader? sr = null;
            FileSystemWatcher fsw;
            try {
                configPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                configPath = Path.Combine(configPath, "Genshin renamer service settings.txt");
                sr = new StreamReader(configPath, Encoding.UTF8, true);
                while (!sr.EndOfStream) {
                    watchedFolder = sr.ReadLine();
                    if (!string.IsNullOrEmpty(watchedFolder)) {
                        fsw = new FileSystemWatcher(watchedFolder);
                        fsw.NotifyFilter = NotifyFilters.CreationTime  | 
                                           NotifyFilters.DirectoryName | 
                                           NotifyFilters.FileName      | 
                                           NotifyFilters.LastAccess    | 
                                           NotifyFilters.LastWrite;
                        fsw.IncludeSubdirectories = false;
                        fsw.Created += FSW_Created;
                        fsw.Error   += FSW_Error;
                        mFSWs.Add(fsw);
                    }
                }
                sr.Close();
                mLogger.LogInformation("Genshin renamer service settings loaded. Number of watched directories = {0}.", mFSWs.Count);
            }
            catch (FileNotFoundException ex) {
                mLogger.LogError(ex, "File 'Genshin renamer service settings.txt' was not found.");
            }
            catch (DirectoryNotFoundException ex) {
                mLogger.LogError(ex, "Directory where 'Genshin renamer service settings.txt' resides was not found.");
            }
            catch (PathTooLongException ex) {
                mLogger.LogError(ex, "Directory where 'Genshin renamer service settings.txt' resides is too deep down. Path too long.");
            }
            catch (IOException ex) {
                mLogger.LogError(ex, "0x{0:X8}. {1}", ex.HResult, ex.Message);
            }
            catch (ArgumentException ex) {
                mLogger.LogError(ex, "Argument exception. {0}", ex.Message);
            }
            catch (UnauthorizedAccessException ex) {
                mLogger.LogError(ex, "The service account cannot access the file 'Genshin renamer service settings.txt'.");
            }
            finally {
                sr?.Dispose();
            }
        }

        /// <summary>
        ///   Produce new file name as the rename target for <paramref name="OldFileName"/> if it matches the regex pattern. 
        ///   If it doesn't match the regex pattern, this function returns <paramref name="OldFileName"/> unchanged.
        /// </summary>
        /// <param name="OldFileName">Old file name (not including the path) to check.</param>
        /// <returns>
        ///   - If <paramref name="OldFileName"/> matches the regex pattern and contains date information dd_mm_yyyy hh_mm_ss, this function returns the same file name but the date info is reversed to yyyy-mm-dd hh-mm-ss. <br/>
        ///   - Otherwise, this returns <paramref name="OldFileName"/> unchanged.
        /// </returns>
        private string ProduceNewFileName(string OldFileName) {
            string        fileNamePart, extensionPart, newFileName;
            string        year, month, day, hour, minute, seconds;
            Match         regexMatch;
            StringBuilder sb;
            const string HYPHEN = "-";
            regexMatch = mRegex.Match(OldFileName);
            if (regexMatch.Success) {
                fileNamePart  = regexMatch.Groups["fname"].Value;
                day           = regexMatch.Groups["day"].Value;
                month         = regexMatch.Groups["month"].Value;
                year          = regexMatch.Groups["year"].Value;
                hour          = regexMatch.Groups["hour"].Value;
                minute        = regexMatch.Groups["minute"].Value;
                seconds       = regexMatch.Groups["second"].Value;
                extensionPart = regexMatch.Groups["ext"].Value;
                sb = new StringBuilder(64);
                sb.Append(fileNamePart);
                sb.Append(year);   sb.Append(HYPHEN);
                sb.Append(month);  sb.Append(HYPHEN);
                sb.Append(day);    sb.Append(" ");
                sb.Append(hour);   sb.Append(HYPHEN);
                sb.Append(minute); sb.Append(HYPHEN);
                sb.Append(seconds);
                sb.Append(extensionPart);
                newFileName = sb.ToString();
                sb.Clear();
                return newFileName;
            }
            else
                return OldFileName;
        }

        /// <summary>
        ///   Browses all watched folders and initially renames all eligible files manually.
        ///   This is executed before <see cref="FileSystemWatcher"/>s' monitoring starts.
        /// </summary>
        /// <param name="CancelTokenObj">A cancellation token that is triggered when <see cref="IHostedService.StopAsync(CancellationToken)"/> is called.</param>
        private void InitialRenameFilesAction(object? CancelTokenObj) {
            string        folderPath, oldFileName, oldFilePath, newFileName, newFilePath;
            int           i  = 0;
            DirectoryInfo dir;
            FileInfo      fi; 
            CancellationToken stoppingToken;
            FileSystemWatcher      fsw;
            IEnumerable<FileInfo>  files;
            IEnumerator<FileInfo>? fileIterator;
            if (CancelTokenObj is null) 
                stoppingToken = CancellationToken.None;
            else
                stoppingToken = (CancellationToken)CancelTokenObj;
            while (i < mFSWs.Count && !stoppingToken.IsCancellationRequested) {
                fsw = mFSWs[i];
                folderPath = fsw.Path;
                try {
                    dir = new DirectoryInfo(folderPath);
                }
                catch (PathTooLongException) { ++i; continue; }
                catch (ArgumentException)    { ++i; continue; }
                if (dir.Exists) {
                    fileIterator = null;
                    try {
                        files = dir.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly);
                        fileIterator = files.GetEnumerator();
                        while (fileIterator.MoveNext() && !stoppingToken.IsCancellationRequested) {
                            fi = fileIterator.Current;
                            oldFileName = fi.Name;
                            newFileName = ProduceNewFileName(oldFileName);
                            if (!string.Equals(oldFileName, newFileName)) {
                                newFilePath = Path.Combine(folderPath, newFileName);
                                try {
                                    File.Move(fi.FullName, newFilePath);
                                    mLogger.LogInformation("File '{0}' was renamed to '{1}'.", oldFileName, newFileName);
                                }
                                catch (FileNotFoundException ex) {
                                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. File not found.", oldFileName, newFileName);
                                }
                                catch (DirectoryNotFoundException ex) {
                                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Directory not found.", oldFileName, newFileName);
                                }
                                catch (PathTooLongException ex) {
                                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Path too long.", oldFileName, newFileName);
                                }
                                catch (IOException ex) {
                                    if ((ex.HResult & 0x0000FFFF) == 0x00000020 /*sharing violation error - file used by another process*/) {
                                        oldFilePath = Path.Combine(folderPath, oldFileName);
                                        mPendingRenames.Enqueue(new Tuple<string, string>(oldFilePath, newFilePath));
                                        mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}', file is locked.", oldFileName, newFileName);
                                    }
                                    else
                                        mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. 0x{2:X8}. {3}", oldFileName, newFileName, ex.HResult, ex.Message);
                                }
                                catch (ArgumentException ex) {
                                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Argument exception. {2}", oldFileName, newFileName, ex.Message);
                                }
                                catch (UnauthorizedAccessException ex) {
                                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Could not access the file (unauthorized).", oldFileName, newFileName);
                                }
                            } //end if fileName != newFileName
                        } //end while fileIterator.MoveNext()
                    }
                    catch (InvalidOperationException) {
                        //IEnumerable<FileInfo> returned by dir.EnumerateFiles was changed -> stop traversal
                    }
                    finally {
                        fileIterator?.Dispose();
                    }
                } //end if dir.Exists
                ++i;
            } //end while mFSWs
        }
        
        /// <summary>
        ///   Periodically tries to rename the file in [<see cref="mPendingRenames"/>] because possibly this time other applications 
        ///   have finished using these files, so they are now unlocked and can be renamed.
        /// </summary>
        private void PeriodicRenameFilesAction() {
            int i;
            Tuple<string, string> renameInstruction;
            List<Tuple<string, string>> failedRenames = new List<Tuple<string, string>>(2);  //to try again next time
            //execute pending renames in the queue
            while (mPendingRenames.Count > 0) {
                renameInstruction = mPendingRenames.Dequeue();
                try {
                    File.Move(renameInstruction.Item1, renameInstruction.Item2);
                    mLogger.LogInformation("File '{0}' was renamed to '{1}'.", renameInstruction.Item1, renameInstruction.Item2);
                }
                catch (FileNotFoundException ex) {
                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. File not found.", renameInstruction.Item1, renameInstruction.Item2);
                }
                catch (DirectoryNotFoundException ex) {
                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Directory not found.", renameInstruction.Item1, renameInstruction.Item2);
                }
                catch (PathTooLongException ex) {
                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Path too long.", renameInstruction.Item1, renameInstruction.Item2);
                }
                catch (IOException ex) {
                    if ((ex.HResult & 0x0000FFFF) == 0x00000020 /*sharing violation error - file used by another process*/) { 
                        mLogger.LogError(ex, "Still cannot rename file '{0}' into '{1}'. Sharing violation error - file is locked.", renameInstruction.Item1, renameInstruction.Item2);   
                        failedRenames.Add(renameInstruction);
                    }
                    else
                        mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. 0x{2:X8}. {3}", renameInstruction.Item1, renameInstruction.Item2, ex.HResult, ex.Message);
                }
                catch (ArgumentException ex) {
                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Argument exception. {2}", renameInstruction.Item1, renameInstruction.Item2, ex.Message);
                }
                catch (UnauthorizedAccessException ex) {
                    mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Could not access the file (unauthorized).", renameInstruction.Item1, renameInstruction.Item2);
                }
            }
            //re-enqueue failed renames to try again next time
            if (failedRenames.Count > 0) {
                i = 0;
                while (i < failedRenames.Count) {
                    renameInstruction = failedRenames[i];
                    mPendingRenames.Enqueue(renameInstruction);
                    ++i;
                }
            }
        }

        /// <summary>
        /// Event handler for [<see cref="FileSystemWatcher.Created"/>] event. Fires when a new file is added to the watched directory.
        /// </summary>
        /// <param name="sender">Reference to the <see cref="FileSystemWatcher"/> object that raised the event.</param>
        /// <param name="e">A <see cref="FileSystemEventArgs"/> that contains information about newly created file.</param>
        private void FSW_Created(object sender, FileSystemEventArgs e) {
            string folderPath, oldFileName, oldFilePath, newFileName, newFilePath;
            if (e.ChangeType == WatcherChangeTypes.Created && !string.IsNullOrEmpty(e.FullPath) && e.FullPath.Length > 0) {
                folderPath  = ((FileSystemWatcher)sender).Path;
                oldFileName = Path.GetFileName(e.FullPath);
                newFileName = ProduceNewFileName(oldFileName);
                if (!string.Equals(oldFileName, newFileName)) { 
                    newFilePath = Path.Combine(folderPath, newFileName);
                    try {
                        File.Move(e.FullPath, newFilePath);
                        mLogger.LogInformation("File '{0}' was renamed to '{1}'.", oldFileName, newFileName);
                    }
                    catch (FileNotFoundException ex) {
                        mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. File not found.", oldFileName, newFileName);
                    }
                    catch (DirectoryNotFoundException ex) {
                        mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Directory not found.", oldFileName, newFileName);
                    }
                    catch (PathTooLongException ex) {
                        mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Path too long.", oldFileName, newFileName);
                    }
                    catch (IOException ex) {
                        if ((ex.HResult & 0x0000FFFF) == 0x00000020 /*sharing violation error - file used by another process*/) {
                            mLogger.LogInformation("Couldn't rename file '{0}' into '{1}', file is locked by another app. Will try again later.", oldFileName, newFileName);
                            oldFilePath = Path.Combine(folderPath, oldFileName);
                            mPendingRenames.Enqueue(new Tuple<string, string>(oldFilePath, newFilePath));
                        }
                        else
                            mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. 0x{2:X8}. {3}", oldFileName, newFileName, ex.HResult, ex.Message);
                    }
                    catch (ArgumentException ex) {
                        mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Argument exception. {2}", oldFileName, newFileName, ex.Message);
                    }
                    catch (UnauthorizedAccessException ex) {
                        mLogger.LogError(ex, "Couldn't rename file '{0}' into '{1}'. Could not access the file (unauthorized).", oldFileName, newFileName);
                    }
                } //end if regexMatch.Success
            } //end if e.ChangeType == Created
        }

        /// <summary>
        /// Event handler for [<see cref="FileSystemWatcher.Error"/>] event. Fires when a <see cref="FileSystemWatcher"/> is unable to continue monitoring a directory.
        /// </summary>
        /// <param name="sender">Reference to the <see cref="FileSystemWatcher"/> object that raised the event.</param>
        /// <param name="e">An <see cref="ErrorEventArgs"/> that contains information about the error.</param>
        private void FSW_Error(object sender, ErrorEventArgs e) {
            Exception ex;
            if (e.GetException() is not null) {
                ex = e.GetException();
                mLogger.LogError(e.GetException(), "FileSystemWatcher encountered an error. {0} - {1}", ex.GetType().Name, ex.Message);
            }
        }


        #endregion
    }
}
