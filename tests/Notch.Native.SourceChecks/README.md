# Native source checks

Run `python3 scripts/check-native-source.py` from a clean checkout with .NET 10 and NuGet access. Use `--dotnet /path/to/dotnet` when the SDK is installed locally. The script regenerates ignored C# projections under `artifacts/` from the current XAML; generated files cannot silently go stale.

The project compiles the actual Windows App, MainWindow, views, view model, services and interop against Windows SDK/WinUI projections. It also checks XAML XML syntax, named fields, local static resource keys, declared event handlers and property/event member types.

`InitializeComponent` is replaced with an empty stub and **never executed**. This check does not perform the Windows XAML compiler's complete validation, create windows, render text, validate input or measure performance. The normal Windows CI build and independent Windows 10/11 interactive qualification remain required.
