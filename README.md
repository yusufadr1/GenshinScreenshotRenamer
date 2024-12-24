This is a Windows Service ("daemon") that runs automatically in the background to monitor your game DVR folder for captured screenshots image files. By default, Windows game DVR stores your screenshot as

    game name dd_mm_yyyy hh_mm_ss.png

and this service should rename files like that into

    game name yyyy-mm-dd hh-mm-ss.png

so when you sort them by name, you also cause them to become sorted by date.
![Example of renamed file](https://github.com/user-attachments/assets/1a0a3922-81d1-4a21-b418-db6c6c33dd92)

This project is named "Genshin" because I play Genshin Impact when I made this, but it is not limited to Genshin. The regex does not care about game name, it cares about the dd_mm_yyyy part.

# Usage
1. Git clone, restore NuGet, set configuration to "Release", and build this Windows Service using Visual Studio 2022 or later.
2. Go to \bin\Release\net8.0\ and you should see the .exe and lots of .dll files.
3. Copy all files of this directory to somewhere else of your liking. For example, `C:\Genshin renamer`.
4. Install this as a service through **Administrator** command prompt.
   1. `sc create GenshinRenSvc type= own start= delayed-auto error= normal binpath= "C:\Genshin renamer\RenamerService.exe" displayname= "Genshin renamer service"`
   2. Notice there is a space after any argument having equal sign, such as `type= own`. This is required by sc.exe.
   3. `sc description GenshinRenSvc "Renames Genshin Impact screenshot image files to make their names have sortable date time."`
   4. Genshin renamer service is now registered as a Windows service. However, it is run by "Local System".
5. Make a new user account through **`lusrmgr.msc`** (run as administrator).
6. For example, I created a user account named `GenshinRenamerSvc`. Give it a password, and make sure you **untick** _User must change password at next login_.
7. Open **`secpol.msc`** (run as administrator). Go to Local policies\User rights assignment. On the right pane, find an entry that says _Log on as a service_.
8. Double click that entry, and add your new user account to the list. We are effectively making that user account a "Windows service account".
9. Open **`services.msc`** (run as administrator). Find Genshin renamer service, right click, _Properties_.
10. Go to the _Log on_ tab. Change from local system account into your windows service account.
11. In your .exe directory, `C:\Genshin renamer` in this example, find the text file `Genshin renamer service settings.txt`
12. Edit this text file. Put your folder path where game screenshots are stored. You can add multiple folders to monitor and rename their files automatically, one folder path on each line.
13. If your game screenshot folder is inside your user home directory (e.g. `C:\Users\your-name\...`), this is usually a protected folder and is restricted to your user account only. Therefore you need to give access to the windows service account.
    1. In the home root directory, give "read" and "list folder content" access to the windows service account.
    2. In the real game DVR directory, on top of "read" and "list folder content", please give "modify" permission too. Without any write permission, this Windows service cannot rename files.
14. Open a File explorer window of your game DVR directory. Start Genshin renamer service. Start your game e.g. Genshin Impact.
15. Try taking a screenshot by `Win`+`Alt`+`PrintScreen`. You should notice a new file is added to your game DVR folder, but that file has the dd_mm_yyyy hh_mm_ss format in its name.
16. In maximum 20 seconds of wait time, this Genshin renamer service should've already renamed that file to yyyy-mm-dd hh-mm-ss format.
17. After next restart, this service should've run automatically and you can start your game immediately.  (Service startup type was included in the original `sc create` command; we specified `start= delayed-auto` hence it should show startup type "Automatic (Delayed start)".)

![lusrmgr.msc Local user manager](https://github.com/user-attachments/assets/05a44ed4-ea08-40a2-ac83-008b2d6d908a)

![secpol.msc Local security policy](https://github.com/user-attachments/assets/c9b6d2eb-020d-4385-890b-f1ecbf109636)

![services.msc Servies](https://github.com/user-attachments/assets/bd3fa710-0ada-4b3e-9afd-a5383311411e)

![Permission to Windows service account](https://github.com/user-attachments/assets/ce5458e0-ca98-44f9-8e00-aedd4624418a)
