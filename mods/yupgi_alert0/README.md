# Over Powered Mod (엽기얼럿 제로)

* See wiki for changes in units : https://github.com/forcecore/yupgi_alert0/wiki
* To run the mod...
  * TODO: UPDATE THESE INSTRUCTIONS
  * put yupgi\_alert.oramod in mods folder, such as
  * C:\\Program Files (x86)\\OpenRA-20170527\\mods
  * On Linux, I think it is ~/.openra/mods
  * Then put OpenRA.yupgi\_alert.exe to where OpenRA.Game.exe is.
  * On Windows, C:\\Program Files (x86)\\OpenRA-20170527
  * Run the mod withe new .exe file. No parameter required.
    * .exe contains two modifications.
    * Skyboxed map support: https://www.youtube.com/watch?v=sFV7S5zTavY
    * OpenRA.{ModName}.exe support: just plug your mod name there and it will run
      the mod without requiring additional parameters!
* You can report bugs at the issues menu : https://github.com/forcecore/yupgi_alert0/issues
* This mod is currently for OpenRA bleed, 30e5c807b061cfa3f571e53ae347359e7be9d4ca,
  * That's around 2021-01-23

The following are for modders.

# Developing the Engine:

* See DEV.md for some instructions.
* If you are looking for the source code of the mod's engine, please visit https://github.com/forcecore/OpenRA

# Utils

* https://github.com/OpenRA/CameoTextEmbedder : GIMP plugin for RA1 cameo text embedding. Started by me and now part of OpenRA utils!
  * Demo: https://www.youtube.com/watch?v=IHOe-uCPbh8
* assets/shp/recolor.py : Script for replacing colors. e.g., testing house colors
* assets/shp/vxl2shp.py : Script to convert vxl to shp.
* https://github.com/forcecore/pylua : compiles lua/ai.py to lua/ai.lua (!!!)

# Acknowledgements

* I'd like to thank Nolt for his graphics! He crafted new graphics just for this mod, including:
  * Infester
  * Super sonic aircraft
* More credits in ART_CREDITS.txt
