using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;

// Lets the test project reach internal classes (SettingsStore's test constructor, TryParseTag,
// BuildCommandLine) without reflection. The assembly is not strong-named, so no public key is needed.
[assembly: InternalsVisibleTo("QuickPane.Tests")]

[assembly: AssemblyTitle("QuickPane")]
[assembly: AssemblyDescription("Explorer sidebar replacement for Windows 10 LTSC")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("PlexWebs")]
[assembly: AssemblyProduct("QuickPane")]
[assembly: AssemblyCopyright("Copyright 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]

// WPF theme/location info. Generic + source assembly fallback for ResourceDictionary lookups.
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly)]

[assembly: AssemblyVersion("3.11.0.1")]
[assembly: AssemblyFileVersion("3.11.0.1")]
