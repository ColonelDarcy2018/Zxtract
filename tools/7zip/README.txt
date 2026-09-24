Place 7z.exe from the official 7-Zip distribution in this folder
so the app can run in a portable layout.

The repository currently uses the official 7-Zip 25.01 binaries without
modification. Their full upstream license and redistribution notice are stored
at ../../licenses/7-Zip-License.txt and must be included in binary packages.

Optional environment variable:
- EXTRACTUTIL_7Z_PATH can point to a custom 7z.exe path.
