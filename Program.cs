using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Cocona;
using Humanizer;
using Humanizer.Localisation;

var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

//todo: ~/punchchamp.yaml or env
var apikey = File.ReadAllText($"{homeDirectory}/.punchchamp.json");

var baseUrl = @"https://connect.signl4.com/api/v2";

var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("x-s4-api-key", apikey);

var app = CoconaApp.Create();

app.AddCommand("info", async (string user) =>
{
  var userData = await httpClient.GetFromJsonAsync<UserData>($"{baseUrl}/users/{user}");

  var name = string.IsNullOrWhiteSpace(userData.Name) ? userData.Mail : userData.Name;

  if (userData!.IsPunchedIn)
  {
    Console.WriteLine($"🚀 User \"{name}\" is punched in.");
    Console.WriteLine($"\u23f0 Started: {userData.LastChange:HH:mm:ss [dd.MM.yyyy]}");

    var activeDuration = DateTime.Now - userData.LastChange;
    
    //todo: duration in rot bei über 8h,warnung bei über 8h :p
    //todo: punchout notifcation count. un bestätige notificatuions.
    Console.WriteLine($"\u23f3 Duration: {activeDuration.Humanize(2, minUnit: TimeUnit.Second)}");
    return;
  }
  
  Console.WriteLine($"\ud83d\udecc User \"{name}\" is punched out.");
  Console.WriteLine($"\u23f0 End: {userData.LastChange:HH:mm:ss [dd.MM.yyyy]}");
});

app.AddCommand("punch-in", () =>
{
  
});

app.AddCommand("punch-out", () =>
{
  
});

app.Run();

record UserData(string Id, string Name, string Mail, DutyInfo[] DutyInfos)
{
  public bool IsPunchedIn => this.DutyInfos[0].DutyMode != DutyMode.None;
  public DateTime LastChange => this.DutyInfos[0].LastChange.LocalDateTime;
}

record DutyInfo(DateTimeOffset LastChange, string TeamId, DutyMode DutyMode, string Name, string Mail);

enum DutyMode
{
  None = 0,
  Primary = 1,
  Manager = 100,
}