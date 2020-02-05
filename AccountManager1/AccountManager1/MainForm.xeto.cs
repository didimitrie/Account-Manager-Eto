using System;
using System.Collections.Generic;
using Eto.Forms;
using Eto.Drawing;
using Eto.Serialization.Xaml;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Timers;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using SpeckleCore;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Linq;

namespace AccountManager1
{
  public class MainForm : Form
  {

    public GridView AccountsView;

    public SelectableFilterCollection<AccountGridItem> filteredCollection;

    public bool IsDialog = false;

    public MainForm(bool OpenAsDialog = false)
    {
      IsDialog = OpenAsDialog;

      DataContext = new AccountManagerDataContext(this);

      Eto.Style.Add<Label>("small-text", label =>
      {
        label.Font = new Font(SystemFont.Default, 10, FontDecoration.None);
      });

      Eto.Style.Add<Label>("large-text", label =>
      {
        label.Font = new Font(SystemFont.Default, 16, FontDecoration.None);
      });

      XamlReader.Load(this);

      AccountsView.Columns.Add(new GridColumn() { HeaderText = "Server Name", DataCell = new TextBoxCell("ServerName"), Width = 170, AutoSize = false, Sortable = true });
      AccountsView.Columns.Add(new GridColumn() { HeaderText = "Email", DataCell = new TextBoxCell("Email"), Width = 150, AutoSize = false, Sortable = true });
      AccountsView.Columns.Add(new GridColumn() { HeaderText = "Default", DataCell = new CheckBoxCell("Default"), Editable = true, Sortable = true });
      AccountsView.Columns.Add(new GridColumn() { HeaderText = "Delete", DataCell = new DeleteCell() });

      SetAccViewDataStore();
    }

    public void SetAccViewDataStore()
    {
      var accs = LocalContext.GetAllAccounts().Select(acc => new AccountGridItem(acc, this));
      filteredCollection = new SelectableFilterCollection<AccountGridItem>(AccountsView, accs);
      AccountsView.DataStore = filteredCollection;
    }

    public void RemoveAccount(AccountGridItem acc)
    {
      filteredCollection.Remove(acc);
    }

    public void AddAccount(Account acc)
    {
      filteredCollection.Add(new AccountGridItem(acc, this));
    }

    public void HandleAccDefaultSet(int id)
    {
      foreach (var accW in filteredCollection)
      {
        if (accW.Id != id)
        {
          accW.Default = false;
        }
      }
      filteredCollection.Refresh();
    }

    protected void HandleLoginClick(object sender, EventArgs e)
    {
      ((AccountManagerDataContext)DataContext).SignInClick();
    }

    public void AccDoubleClick(object sender, EventArgs e)
    {
      var ee = ((GridCellMouseEventArgs)e).Item as AccountGridItem;
      if (IsDialog)
      {
        // TODO: return
      }
      var result = MessageBox.Show($"Account: {ee.Email} @ {ee.ServerName} {ee.Account.RestApi}", MessageBoxButtons.OK, MessageBoxType.Information);
    }

  }

  class DeleteCell : CustomCell
  {
    AccountGridItem account;

    protected override Control OnCreateCell(CellEventArgs args)
    {
      var control = new Button() { Text = "🚫", Size = new Size(10, 10) };
      control.BindDataContext(c => c.Command, (AccountGridItem acc) => acc.DeleteAccount);
      return control;
    }


  }

  public class AccountManagerDataContext : INotifyPropertyChanged
  {
    #region Properties
    MainForm Parent;

    string serverUrl = @"https://hestia.speckle.works", _apiUri = @"https://hestia.speckle.works/api", _serverUri = @"https://hestia.speckle.works";
    public string ServerUrl
    {
      get => serverUrl;
      set
      {
        serverUrl = value;
        NotifyPropertyChanged();
        UrlVerificationTimer.Start();
      }
    }

    string urlCheckStatus = "";
    public string UrlCheckStatus { get => urlCheckStatus; set { urlCheckStatus = value; NotifyPropertyChanged(); } }

    bool enableLoginButton = false;
    public bool EnableLoginButton { get => enableLoginButton; set { enableLoginButton = value; NotifyPropertyChanged(); } }

    public Timer UrlVerificationTimer = new Timer(500);
    public bool urlVerificationInProgress = false;
    #endregion

    public AccountManagerDataContext(MainForm parent)
    {
      Parent = parent;

      UrlVerificationTimer.AutoReset = false;
      UrlVerificationTimer.Elapsed += UrlVerification_Elapsed;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Url Verification
    private void UrlVerification_Elapsed(object sender, ElapsedEventArgs e)
    {
      Application.Instance.Invoke(() => { UrlCheckStatus = "Checking server url..."; EnableLoginButton = false; });

      CheckServerUrl(ServerUrl);
    }

    async Task CheckServerUrl(string url)
    {
      url = url.Trim();

      Uri baseUri;
      bool result = Uri.TryCreate(url, UriKind.Absolute, out baseUri)
          && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps);

      if (!result)
      {
        Application.Instance.Invoke(() => { UrlCheckStatus = "🚫 No Speckle Server found."; EnableLoginButton = false; });
        return;
      }

      try
      {
        _serverUri = baseUri.Scheme + "://" + baseUri.Host;

        if (!baseUri.IsDefaultPort) { _serverUri += ":" + baseUri.Port; }

        _apiUri = _serverUri + "/api";

        var request = (HttpWebRequest)WebRequest.Create(new Uri(_apiUri));
        request.Timeout = 300;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream))
        {
          var test = reader.ReadToEnd();
          var tes2 = test;
          if (test.Contains("isSpeckleServer"))
          {
            //isCorrectUrl = true;
            Application.Instance.Invoke(() => { UrlCheckStatus = "✅ Detected Speckle Server."; EnableLoginButton = true; });
          }
          else
          {
            //isCorrectUrl = false;
            Application.Instance.Invoke(() => { UrlCheckStatus = "🚫 No Speckle Server found."; EnableLoginButton = false; });
          }
        }
      }
      catch (Exception err)
      {
        Application.Instance.Invoke(() => { UrlCheckStatus = "🚫 No Speckle Server found."; EnableLoginButton = false; });
        //isCorrectUrl = false;
      }
    }
    #endregion

    #region New Accounts

    HttpListener listener;
    Process browser;

    public void SignInClick()
    {
      Task.Run(() =>
      {
        browser = Process.Start(_serverUri + "/signin?redirectUrl=http://localhost:5050");
        InstantiateWebServer();
      });
    }

    void InstantiateWebServer()
    {
      if (listener != null)
      {
        listener.Abort();
      }
      try
      {
        listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5050/");
        listener.Start();

        var ctx = listener.GetContext();
        //this is where it stops to wait for a request
        var req = ctx.Request;
        listener.Stop();

        try
        {
          browser.CloseMainWindow();
          browser.Close();
        }
        catch (Exception e)
        {
          Debug.WriteLine(e);
        }

        AddAccountFromRedirect(ctx.Request.Url.Query);
      }
      catch (Exception e)
      {
        Debug.WriteLine(e);
        MessageBox.Show("Speckle had a problem adding your account; Let us know what happened. \n This is what the computer says: \n" + e.Message);
      }
    }

    void AddAccountFromRedirect(string redirect)
    {
      var myString = Uri.UnescapeDataString(redirect);

      var splitRes = myString.Replace("?token=", "").Split(new[] { ":::" }, StringSplitOptions.None);
      var token = splitRes[0];
      var serverUrl = splitRes[1];

      var apiCl = new SpeckleApiClient(_apiUri) { AuthToken = token };
      var res = apiCl.UserGetAsync().Result;

      var apiToken = res.Resource.Apitoken;
      var email = res.Resource.Email;

      SaveOrUpdateAccount(new Account() { RestApi = apiCl.BaseUrl, Email = email, Token = apiToken });
    }

    public void SaveOrUpdateAccount(Account newAccount)
    {
      var existingAccounts = LocalContext.GetAllAccounts();
      var newUri = new Uri(newAccount.RestApi);

      var serverName = GetServerName(newUri);

      foreach (var acc in existingAccounts)
      {
        var eUri = new Uri(acc.RestApi);
        if ((eUri.Host == newUri.Host) && (acc.Email == newAccount.Email) && (eUri.Port == newUri.Port))
        {
          acc.ServerName = serverName;
          acc.Token = newAccount.Token;
          LocalContext.UpdateAccount(acc);
          showSuccessBox(acc.ServerName, acc.RestApi);
          return;
        }
      }
      newAccount.ServerName = serverName;
      LocalContext.AddAccount(newAccount);
      Application.Instance.Invoke(() => { Parent.AddAccount(newAccount); });
      showSuccessBox(newAccount.ServerName, newAccount.RestApi);
    }

    void showSuccessBox(string serverName, string restApi)
    {
      Application.Instance.Invoke(() =>
      {
        MessageBox.Show($"🎉 Succesfully added your account at {serverName} ({restApi})!");
      });
    }

    string GetServerName(Uri serverApi)
    {
      using (var cl = new WebClient())
      {
        var response = JsonConvert.DeserializeObject<dynamic>(cl.DownloadString(serverApi));
        return Convert.ToString(response.serverName);
      }
      throw new Exception("Could not get server name.");
    }

    #endregion

  }

  public class AccountGridItem : INotifyPropertyChanged
  {
    public Account Account;
    MainForm Parent;

    public int Id { get => Account.AccountId; }

    public string Email
    {
      get => Account.Email;
    }

    public string ServerName
    {
      get => Account.ServerName;
    }

    public bool? Default
    {
      get => Account.IsDefault;
      set
      {
        Account.IsDefault = (bool)value;
        if (value == true)
        {
          LocalContext.SetDefaultAccount(Account);
          Application.Instance.Invoke(() =>
          {
            Parent.HandleAccDefaultSet(Account.AccountId);
          });
        }
        LocalContext.UpdateAccount(Account);
        NotifyPropertyChanged();
      }
    }

    public AccountGridItem(Account acc, MainForm parent)
    {
      Account = acc;
      Parent = parent;
    }

    Command delete;

    public Command DeleteAccount
    {
      get => delete ?? (delete = new Command((sender, e) =>
      {
        var result = MessageBox.Show($"Delete account: {Email} @ {ServerName}? You can't undo this action.", MessageBoxButtons.YesNo, MessageBoxType.Warning);
        if (result == DialogResult.Yes)
        {
          LocalContext.RemoveAccount(this.Account);
          Application.Instance.Invoke(() =>
          {
            Parent.RemoveAccount(this);
          });
        }
      }));
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
  }
}
