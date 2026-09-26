using System.Windows;
using System.Windows.Input;
using LocalSocialArchive.Services;

namespace LocalSocialArchive.Views;

public partial class UnlockWindow : Window
{
    private readonly ISecurityService _securityService;

    public UnlockWindow(ISecurityService securityService)
    {
        InitializeComponent();
        _securityService = securityService;

        Loaded += (s, e) =>
        {
            TxtPassword.Focus();
        };
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnUnlock_Click(object sender, RoutedEventArgs e)
    {
        PerformUnlock();
    }

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PerformUnlock();
        }
    }

    private void PerformUnlock()
    {
        string pwd = TxtPassword.Password;
        if (_securityService.VerifyPassword(pwd))
        {
            ErrorBorder.Visibility = Visibility.Collapsed;
            DialogResult = true;
            Close();
        }
        else
        {
            TxtError.Text = "Incorrect master password. Please try again.";
            ErrorBorder.Visibility = Visibility.Visible;
            TxtPassword.SelectAll();
            TxtPassword.Focus();
        }
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
