using Notch.Core.Tests;

var suite = new TestSuite();
PreferenceCases.Register(suite);
TimerCases.Register(suite);
OverlayCases.Register(suite);
OverlayGeometryCases.Register(suite);
HoverInteractionCases.Register(suite);
MediaSourceIdentityCases.Register(suite);
StoreCases.Register(suite);
WorkspaceCases.Register(suite);
ConverterCases.Register(suite);
ProviderCases.Register(suite);
ConnectionCases.Register(suite);
CalendarCases.Register(suite);
CodingCases.Register(suite);
return await suite.RunAsync();
