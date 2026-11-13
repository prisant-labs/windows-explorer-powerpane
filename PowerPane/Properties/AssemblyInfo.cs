using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;

// Lets the test project reach internal classes (SettingsStore's test constructor, TryParseTag,
// BuildCommandLine) without reflection. The assembly is not strong-named, so no public key is needed.
[assembly: InternalsVisibleTo("PowerPane.Tests")]

[assembly: AssemblyTitle("PowerPane")]
[assembly: AssemblyDescription("Pinned folders inside File Explorer and file dialogs")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Prisant Labs")]
[assembly: AssemblyProduct("Explorer PowerPane")]
[assembly: AssemblyCopyright("Copyright 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]

// WPF theme/location info. Generic + source assembly fallback for ResourceDictionary lookups.
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly)]

[assembly: AssemblyVersion("4.0.0.1")]
[assembly: AssemblyFileVersion("4.0.0.1")]
