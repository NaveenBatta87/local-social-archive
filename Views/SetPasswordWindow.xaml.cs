using System.Windows;
using System.Windows.Input;
using LocalSocialArchive.Services;

namespace LocalSocialArchive.Views;

public partial class SetPasswordWindow : Window
{
    private readonly ISecurityService _securityService;

    public SetPasswordWindow(ISecurityService securityService)
    {
        InitializeComponent();
        _securityService = securityService;

        if (_securityService.HasPasswordSet)
        {
            CurrentPasswordPanel.Visibility = Visibility.Visible;
            BtnRemovePassword.Visibility = Visibility.Visible;
        }

        Loaded += (s, e) =>
        {
            if (_securityService.HasPasswordSet)
            {
                TxtCurrentPassword.Focus();
            }
            else
            {
                TxtNewPassword.Focus();
            }
        };
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ErrorBorder.Visibility = Visibility.Collapsed;

        if (_securityService.HasPasswordSet)
        {
            string currentPwd = TxtCurrentPassword.Password;
            if (!_securityService.VerifyPassword(currentPwd))
            {
                TxtError.Text = "Current password is incorrect.";
                ErrorBorder.Visibility = Visibility.Visible;
                TxtCurrentPassword.SelectAll();
                TxtCurrentPassword.Focus();
                return;
            }
        }

        string newPwd = TxtNewPassword.Password;
        string confirmPwd = TxtConfirmPassword.Password;

        if (string.IsNullOrWhiteSpace(newPwd))
        {
            TxtError.Text = "New password cannot be blank.";
            ErrorBorder.Visibility = Visibility.Visible;
            TxtNewPassword.Focus();
            return;
        }

        if (newPwd != confirmPwd)
        {
            TxtError.Text = "Passwords do not match. Please re-enter.";
            ErrorBorder.Visibility = Visibility.Visible;
            TxtConfirmPassword.SelectAll();
            TxtConfirmPassword.Focus();
            return;
        }

        _securityService.SetPassword(newPwd);
        DialogResult = true;
        Close();
    }

    private void BtnRemovePassword_Click(object sender, RoutedEventArgs e)
    {
        ErrorBorder.Visibility = Visibility.Collapsed;

        string currentPwd = TxtCurrentPassword.Password;
        if (!_securityService.VerifyPassword(currentPwd))
        {
            TxtError.Text = "Enter your current password to remove protection.";
            ErrorBorder.Visibility = Visibility.Visible;
            TxtCurrentPassword.Focus();
            return;
        }

        var result = MessageBox.Show(
            "Are you sure you want to remove password protection from your offline archive?",
            "Remove Password",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _securityService.RemovePassword();
            DialogResult = true;
            Close();
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
