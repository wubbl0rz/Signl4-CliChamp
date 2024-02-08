using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Cocona;
using Humanizer;
using Humanizer.Localisation;
using Spectre.Console;

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

  if (userData!.IsPunchedIn)
  {
    Console.WriteLine($"🚀 User \"{userData.DisplayName}\" is punched in.");
    Console.WriteLine($"\u23f0 Started: {userData.LastChange:HH:mm:ss [dd.MM.yyyy]}");

    var activeDuration = DateTime.Now - userData.LastChange;
    
    //todo: duration in rot bei über 8h,warnung bei über 8h :p
    //todo: punchout notifcation count. un bestätige notificatuions.
    Console.WriteLine($"\u23f3 Duration: {activeDuration.Humanize(2, minUnit: TimeUnit.Second)}");
    return;
  }
  
  Console.WriteLine($"\ud83d\udecc User \"{userData.DisplayName}\" is punched out.");
  Console.WriteLine($"\u23f0 End: {userData.LastChange:HH:mm:ss [dd.MM.yyyy]}");
});

app.AddCommand("punch-in", async (string user, DateTime? till, CoconaAppContext ctx) =>
{
  var userData = await httpClient.GetFromJsonAsync<UserData>($"{baseUrl}/users/{user}");

  // if (userData!.IsPunchedIn)
  // {
  //   Console.WriteLine($"\ud83d\udc40 User \"{userData.DisplayName}\" is already punched in.");
  //   Console.WriteLine($"\u23f0 Started: {userData.LastChange:HH:mm:ss [dd.MM.yyyy]}");
  //
  //   var activeDuration = DateTime.Now - userData.LastChange;
  //   
  //   Console.WriteLine($"\u23f3 Duration: {activeDuration.Humanize(2, minUnit: TimeUnit.Second)}");
  //   return;
  // }
  
  await httpClient.PostAsJsonAsync($"{baseUrl}/duties/punchIn",
    new PunchInData(userData!.Id, [userData.TeamId]));
  
  // AnsiConsole.Status()
  //   .Spinner(Spinner.Known.Star)
  //   .Start("Thinking...", ctx => {
  //     // Omitted
  //   });
  
  var remaining = till?.Subtract(DateTime.Now);
  
  await AnsiConsole.Progress()
    .Columns(new SpinnerColumn(), new PercentageColumn(), new RemainingTimeColumn())
    .StartAsync(async progress =>
    {
      var task = progress.AddTask($"progress");

      while (!ctx.CancellationToken.IsCancellationRequested)
      {
        if (DateTime.Now >= till)
        {
          await httpClient.PostAsJsonAsync($"{baseUrl}/duties/punchOut",
            new PunchInData(userData!.Id, [userData.TeamId]));
      
          break;
        }
        
        task.Increment(remaining.Value.TotalSeconds);
      
        await Task.Delay(1000);
      }
    });

  // await AnsiConsole.Progress()
  //   .StartAsync(async progress =>
  //   {
  //     var task1 = progress.AddTask("[green]Reticulating splines[/]");
  //
  //     while (!ctx.CancellationToken.IsCancellationRequested)
  //     {
  //       if (DateTime.Now >= till)
  //       {
  //         await httpClient.PostAsJsonAsync($"{baseUrl}/duties/punchOut",
  //           new PunchInData(userData!.Id, [userData.TeamId]));
  //     
  //         break;
  //       }
  //       
  //       task1.Increment(1);
  //
  //       await Task.Delay(1000);
  //     }
  //   });
  
  //todo: show time remaining
});

app.AddCommand("punch-out", () =>
{
  
});

app.Run();

record UserData(string Id, string Name, string Mail, DutyInfo[] DutyInfos)
{
  public string TeamId => this.DutyInfos[0].TeamId;
  public bool IsPunchedIn => this.DutyInfos[0].DutyMode != DutyMode.None;
  public DateTime LastChange => this.DutyInfos[0].LastChange.LocalDateTime;
  public string DisplayName => string.IsNullOrWhiteSpace(this.Name) ? this.Mail : this.Name;
}

record PunchInData(string UserId, string[] TeamIds);

record DutyInfo(DateTimeOffset LastChange, string TeamId, DutyMode DutyMode, string Name, string Mail);

enum DutyMode
{
  None = 0,
  Primary = 1,
  Manager = 100,
}