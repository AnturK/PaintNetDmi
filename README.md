# Paint<span></span>.Net DMI FileType Extension

# Installation:
Copy `PaintNetDmi.dll` from Releases page into `FileTypes` directory in your Paint<span></span>.Net installation directory. (For example `C:\Program Files\paint.net\FileTypes`)

## Features:
* Loading DMI states into separate layers
* Editing states and their names
* Creating new DMI from scratch (Assumes one-direction,no animations and square frame dimensions)

## Does not support:
* Editing/creating animations and animation properties.
* Adding/Removing new states

# Building :
Since Paint<span></span>.Net does not provide ref assemblies in any automated way they need to be copied manually from Paint<span></span>.Net installation directory.
Copy "PaintDotNet.Base.dll","PaintDotNet.Core.dll","PaintDotNet.Data.dll" assemblies into Lib directory before building.