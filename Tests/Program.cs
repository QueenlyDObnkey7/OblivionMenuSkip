using OblivionMenuSkip;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAILED: " + name);
    checks++; Console.WriteLine("PASS " + name);
}
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, name);
}
using var reader = File.OpenText(Path.Combine(AppContext.BaseDirectory, "Native.tsv"));
var layouts = NativeLayoutTable.Read(reader);
var required = new[] { "Conv_StringToName", "GetPlayerController", "StartCameraFade", "GetCurrentWorld",
    "GetButtonsVisibility", "SetButtonsVisibility", "GetLevelChangeData", "QuickLoadSaveAfterFadeToBlack", "IsVisible" };
Check(layouts.Keys.Order().SequenceEqual(required.Order()), "Only the nine menu-entry native contracts are included");
Check(layouts["QuickLoadSaveAfterFadeToBlack"].Size == 0, "Starter load has no invented arguments");
Check(layouts["StartCameraFade"].Fields["bHoldWhenFinished"] == (29, 1), "Fade contract retains its hold flag");
bool duplicateRejected = false;
try { NativeLayoutTable.Read(new StringReader("IsVisible|1|ReturnValue:0:1\nIsVisible|1|ReturnValue:0:1")); }
catch (InvalidDataException) { duplicateRejected = true; }
Check(duplicateRejected, "Duplicate native registrations fail validation");

var temp = Path.Combine(Path.GetTempPath(), "MenuSkip-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(temp, "dlls"));
var host = Path.Combine(temp, "dlls", "OblivionMpCSharpMod.dll");
Reject(() => StarterFile.Validate(""), "Missing host location is rejected");
Reject(() => StarterFile.Validate(Path.Combine(temp, "unexpected", "host.dll")), "Unsupported host layout is rejected");
Reject(() => StarterFile.Validate(host), "Missing starter never initiates loading");
var starter = Path.Combine(temp, "world.sav");
File.WriteAllBytes(starter, []);
Reject(() => StarterFile.Validate(host), "Empty starter never initiates loading");
byte[] bytes = [1, 2, 3, 4];
File.WriteAllBytes(starter, bytes);
Check(StarterFile.Validate(host) == starter, "Starter resolves beside the host dlls directory");
Check(File.ReadAllBytes(starter).SequenceEqual(bytes), "Validation leaves the starter unchanged");
Console.WriteLine($"{checks} checks passed. Test fixtures: {temp}");
