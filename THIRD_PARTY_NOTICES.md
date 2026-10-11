# RouterTray third-party notices

RouterTray is distributed under the MIT license in `LICENSE`.
The self-contained Microsoft Store package also includes the .NET runtime,
Windows Desktop runtime, C#/WinRT support, and BouncyCastle.Cryptography.
Original license and third-party notice files provided by the restored
runtime and library NuGet packages are included in the package's `Licenses`
directory. The Windows SDK targeting-pack terms are linked below. Velopack
is excluded from the Microsoft Store build.

## BouncyCastle.Cryptography 2.7.0

Source: https://github.com/bcgit/bc-csharp/tree/release/v2.7

MIT License (https://opensource.org/licenses/MIT)

Copyright (c) 2000-2026 The Legion of the Bouncy Castle Inc. (https://www.bouncycastle.org).
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sub license, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions: The above copyright notice and this
permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## .NET and Windows integration

.NET runtime and Windows Desktop: https://github.com/dotnet/runtime and
https://github.com/dotnet/windowsdesktop (MIT, with third-party notices).

C#/WinRT runtime: https://github.com/microsoft/CsWinRT (MIT).

MIT License

Copyright (c) Microsoft Corporation.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Windows SDK .NET projection (Microsoft.Windows.SDK.NET.Ref):
Copyright (c) Microsoft Corporation. All rights reserved.
The targeting pack declares these terms: https://aka.ms/WinSDKLicenseURL
Project: https://aka.ms/WinSDKProjectURL

The `Licenses` directory is generated from the restored NuGet packages,
including both runtime packs, rather than from a fixed runtime version.
