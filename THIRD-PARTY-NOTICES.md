# Third-Party Notices

Zxtract itself is licensed under the MIT License. The distributable package
also includes the following third-party software under its own license terms.

## 7-Zip 25.01

Zxtract invokes the official 7-Zip command-line extraction engine and
distributes `7z.exe` and `7z.dll` in the Windows portable package.

- Project: https://www.7-zip.org/
- Copyright: Copyright (C) 1999-2025 Igor Pavlov
- `7z.exe`: GNU Lesser General Public License (LGPL)
- `7z.dll`: GNU LGPL for most code, GNU LGPL with the unRAR restriction for
  some code, and BSD 2-Clause/BSD 3-Clause terms for specified components

Zxtract does not modify the bundled 7-Zip binaries. The full upstream license
and required redistribution information are reproduced in
[`licenses/7-Zip-License.txt`](licenses/7-Zip-License.txt).

The unRAR-derived code may not be used to recreate the proprietary RAR
compression algorithm. See the full upstream notice for the precise terms.
