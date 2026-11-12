# Third-party code in this folder

Everything under `PowerPane/Themes/Fluent/` is ported from Microsoft's WPF Fluent theme,
which ships as `PresentationFramework.Fluent` with .NET 9 WPF and is developed in the open
in the `dotnet/wpf` repository.

- Source repository: https://github.com/dotnet/wpf
- Source commit: `dbd3026982f09a279e4595666c07bb6136f579c3`
- Source path: `src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/`
- Ported folders: `Styles/`, `Resources/`, `Resources/Theme/`, `Controls/`
- Licence: MIT (both notices below apply; see `LICENSE-MIT.txt` in this folder for the full text)

This theme applies to the Settings window only (`PowerPane.Services.FluentThemeService`
merges it into that window's own `Resources`, never into `Application.Current.Resources`),
so the pane and every other window PowerPane shows keep their existing look. `ListView`,
`DataGrid`, and `Window.xaml` were left unported: the Settings window does not use the
first two, and the third needs WPF-internal types that are not part of the public .NET
Framework 4.8 surface.

## Two copyright notices

Every ported `.xaml` file carries this notice in its own header, unchanged:

```
This Source Code Form is subject to the terms of the MIT License.
If a copy of the MIT was not distributed with this file, You can obtain one at
https://opensource.org/licenses/MIT.
Copyright (C) Leszek Pomianowski and WPF UI Contributors.
All Rights Reserved.

Based on Microsoft XAML for Win UI
Copyright (c) Microsoft Corporation. All Rights Reserved.
```

Microsoft's Fluent theme for WPF was built from the open-source WPF-UI project
(`lepoco/wpfui` on GitHub), itself based on Microsoft's WinUI XAML, and the files keep
WPF-UI's own copyright line. This is Microsoft's source as shipped, not a mix-up on
PowerPane's part: both notices are genuinely present in the upstream files, and both are
kept here for that reason.

The one ported `.cs` converter in `Controls/`, `AnimationFactorToValueConverter.cs`, carries
the plainer notice used elsewhere in `dotnet/wpf`:

```
Licensed to the .NET Foundation under one or more agreements.
The .NET Foundation licenses this file to you under the MIT license.
```

Microsoft's source folder has two further converters, `FallbackBrushConverter.cs` and
`ViewIsGridViewConverter.cs`; neither is ported, because nothing in the control subset
above references either one.

## What PowerPane changed

Every change from Microsoft's source is marked with a `PowerPane change` or
`PowerPane addition` comment at the point it was made. In short: an `xmlns:system`
namespace fix (`System.Runtime` does not exist on .NET Framework 4.8; `mscorlib` has the
same types), `StaticResource` references that only resolve in Microsoft's own build (which
flattens every file into one dictionary before shipping) changed to `DynamicResource` or,
for `MenuItem.xaml`'s one cross-file `BasedOn`, a merge of the one sibling file it needs; a
rewrite of the one C# 9 `is not` pattern so the converter compiles under the project's C#
7.3 language version; and the seven `SystemAccentColor*` keys, which Microsoft's file reads
from .NET 9-only `SystemColors` statics, replaced with placeholder `Color` resources that
`PowerPane.Services.FluentThemeService` overwrites with the real Windows accent at load
time.
