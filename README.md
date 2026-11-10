# CPK Extractor

Android app to inspect and extract CRIWARE `.cpk` archives.

## Features
- Select a CPK file using Android's file picker.
- Read the archive's file table and preview file paths.
- Extract files while preserving their folder structure.
- Save the result as a ZIP using Android's save dialog.

## Build
The GitHub Actions workflow **Build Android APK** builds the Android APK. It runs automatically on pushes to `main`, or can be started manually from the Actions tab.

The app uses [CriFsV2Lib](https://github.com/Sewer56/CriFsV2Lib) (LGPL-3.0-or-later) for CPK reading and CRILayla decompression.

## Limitations
Some games encrypt CPK headers or individual files with game-specific keys. The initial version does not guess or bypass unknown encryption schemes. A known P5R decryption option can be added separately after device testing.

This project is an initial build and still needs a successful CI build and testing with real CPK files.
