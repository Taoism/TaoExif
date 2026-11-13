# TaoExif TODO

## Not in v0.1 (ExifToolGui features still missing)
- Drag and drop of files/folders onto the window (Avalonia 12 IDataTransfer API)
- Edit structured XMP (regions, hierarchical keywords) as trees
- Lossless JPEG rotate, and the jpegtran/ImageMagick integrations
- Compare the metadata of two files side by side
- Place search on the map (Nominatim), other tile layers
- Virtualised thumbnail grid for folders with many thousands of files
- Column sorting for the tag columns (currently string sort of Values[i])
- Friendlier date display in the columns (exiftool's "YYYY:MM:DD")
- Localisation

## Release
- Installer (Inno Setup, like TaoSFV) and a build-release.ps1
- Sign/notarize the macOS build; test it on real macOS hardware (only built, not run)
- Linux: AppImage or .desktop entry; perl-less systems need perl or a system exiftool
