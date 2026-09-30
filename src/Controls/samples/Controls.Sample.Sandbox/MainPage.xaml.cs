using Microsoft.Maui.Storage;

namespace Maui.Controls.Sample;

public partial class MainPage : ContentPage
{
	const string NameKey = "sandbox.account.name";
	const string EmailKey = "sandbox.account.email";
	const string NotificationsKey = "sandbox.preferences.notifications";
	const string DigestKey = "sandbox.preferences.digest";
	const string CompactKey = "sandbox.preferences.compact";

	bool _loading;
	string _savedName = "Alex Johnson";
	string _savedEmail = "alex@example.com";

	public MainPage()
	{
		InitializeComponent();
		_loading = true;
		_savedName = Preferences.Default.Get(NameKey, _savedName);
		_savedEmail = Preferences.Default.Get(EmailKey, _savedEmail);
		if (string.IsNullOrWhiteSpace(_savedName))
			_savedName = "Alex Johnson";
		NameEntry.Text = _savedName;
		EmailEntry.Text = _savedEmail;
		NotificationsSwitch.IsToggled = Preferences.Default.Get(NotificationsKey, true);
		DigestSwitch.IsToggled = Preferences.Default.Get(DigestKey, true);
		CompactSwitch.IsToggled = Preferences.Default.Get(CompactKey, false);
		_loading = false;
		UpdateProfile();
		UpdatePreferences();
		Console.WriteLine("SANDBOX: Account and preferences ready");
	}

	void OnSaveClicked(object sender, EventArgs e)
	{
		var name = NameEntry.Text?.Trim() ?? string.Empty;
		var email = EmailEntry.Text?.Trim() ?? string.Empty;
		if (name.Length == 0 || email.Length == 0 || !IsValidEmail(email))
		{
			AccountStatus.Text = "Enter a name and a valid email address.";
			AccountStatus.TextColor = Color.FromArgb("#B3261E");
			Console.WriteLine("SANDBOX: Account validation failed");
			return;
		}

		_savedName = name;
		_savedEmail = email;
		NameEntry.Text = name;
		EmailEntry.Text = email;
		Preferences.Default.Set(NameKey, name);
		Preferences.Default.Set(EmailKey, email);
		UpdateProfile();
		AccountStatus.Text = "Changes saved on this device.";
		AccountStatus.TextColor = Color.FromArgb("#386A20");
		Console.WriteLine("SANDBOX: Account saved");
	}

	void OnDiscardClicked(object sender, EventArgs e)
	{
		NameEntry.Text = _savedName;
		EmailEntry.Text = _savedEmail;
		AccountStatus.Text = "Unsaved changes discarded.";
		AccountStatus.TextColor = Color.FromArgb("#49454F");
		Console.WriteLine("SANDBOX: Account changes discarded");
	}

	static bool IsValidEmail(string email)
	{
		// Lightweight sample validation; no remote account or authentication is implied.
		int at = email.IndexOf('@');
		int dot = email.LastIndexOf('.');
		return at > 0 && dot > at + 1 && dot < email.Length - 1
			&& !email.Contains(' ') && email.IndexOf('@', at + 1) == -1;
	}

	void UpdateProfile()
	{
		ProfileName.Text = _savedName;
		ProfileEmail.Text = _savedEmail;
		var words = _savedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		AvatarInitials.Text = words.Length > 1
			? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[^1][0])}"
			: char.ToUpperInvariant(words[0][0]).ToString();
		PreviewTitle.Text = $"Good to see you, {words[0]}";
	}

	void OnNotificationsToggled(object sender, ToggledEventArgs e)
	{
		if (_loading)
			return;

		Preferences.Default.Set(NotificationsKey, e.Value);
		UpdatePreferences();
		Console.WriteLine($"SANDBOX: Notifications {(e.Value ? "on" : "off")}");
	}

	void OnDigestToggled(object sender, ToggledEventArgs e)
	{
		if (_loading)
			return;

		Preferences.Default.Set(DigestKey, e.Value);
		UpdatePreferences();
		Console.WriteLine($"SANDBOX: Weekly digest {(e.Value ? "on" : "off")}");
	}

	void OnCompactToggled(object sender, ToggledEventArgs e)
	{
		if (_loading)
			return;

		Preferences.Default.Set(CompactKey, e.Value);
		UpdatePreferences();
		Console.WriteLine($"SANDBOX: Compact layout {(e.Value ? "on" : "off")}");
	}

	void UpdatePreferences()
	{
		NotificationSummary.Text = $"Notifications {(NotificationsSwitch.IsToggled ? "on" : "off")} · " +
			$"Weekly digest {(DigestSwitch.IsToggled ? "on" : "off")}";
		PreviewItems.Spacing = CompactSwitch.IsToggled ? 4 : 14;
	}
}
