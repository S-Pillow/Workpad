using System;

namespace WorkNotes.Services
{
    /// <summary>
    /// Pro feature gating for subscription/license management.
    /// </summary>
    public static class ProFeatures
    {
        /// <summary>
        /// Gets whether the user has access to Split View feature.
        /// TODO: Connect to actual license/subscription system.
        /// </summary>
        public static bool SplitViewEnabled
        {
            get
            {
                // For MVP: always enabled (set to false to test Pro gating)
                // In production: check license key, subscription status, etc.
                return true;
            }
        }

        /// <summary>
        /// Shows an upgrade dialog for Pro features.
        /// </summary>
        public static void ShowUpgradeDialog(string featureName)
        {
            System.Windows.MessageBox.Show(
                $"'{featureName}' is a Pro feature.\n\n" +
                "Upgrade to Work Notes Pro to unlock:\n" +
                "• Split View editing\n" +
                "• Advanced productivity features\n" +
                "• Priority support\n\n" +
                "Visit our website to upgrade.",
                "Upgrade to Pro",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }
}
