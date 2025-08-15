# Giggys6PlayerStraftatMod
Installation Guide
This guide will walk you through installing the mod. The recommended method is to use the Thunderstore Mod Manager, as it handles all dependencies automatically.

Method 1: Thunderstore Mod Manager (Recommended)
This is the easiest and safest way to install the mod.

Download a Mod Manager: Download and install Thunderstore Mod Manager or r2modman. Both are excellent and work the same way.

Select the Game: Open the mod manager and select the game from the list.

Create a Profile: It's best to create a new, clean profile for your mods.

Install BepInEx: From the "Online" tab, search for BepInExPack and install it. This is the modding framework that all other mods depend on.

Download the Mod: Go to the "Releases" section of this GitHub page and download the latest Giggys6playerMod.dll file.

Drop .DLL in this file directory: C:\Users\(USERNAME)\AppData\Roaming\Thunderstore Mod Manager\DataFolder\STRAFTAT\profiles\mod\BepInEx\plugins 

Launch the Game: Click the "Start Modded" button at the top of the mod manager to launch the game with the mod active.

Method 2: Manual Installation (Advanced)
Use this method only if you do not want to use a mod manager.

Prerequisites
You must have BepInEx 5 installed in your game first. You can find instructions on the BepInEx documentation site.

Installation Steps
Download the Mod: Go to the "Releases" section of this GitHub page and download the latest Giggys6playerMod.dll file.

Locate Your Game Folder: Find the installation folder for your game. On Steam, you can do this by right-clicking the game in your library -> Manage -> Browse local files.

Navigate to the Plugins Folder: Inside your game's folder, navigate to the BepInEx/plugins directory. If the plugins folder does not exist, you should run the game once with BepInEx installed to generate it.

Copy the DLL: Place the downloaded Giggys6PlayerMod.dll file directly into the BepInEx/plugins folder.

Launch the Game: Run the game through Steam as you normally would. BepInEx will automatically load the mod.

Configuration
After running the game with the mod at least once, a configuration file will be generated at:
BepInEx/config/com.yourname.gamename.Giggys6playermod.cfg

You can open this file in a text editor to change settings like the maximum player count and the default game mode.
