using System;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace ProfessionalPowerCopyCatalogModern
{
    // Loads the company logo (logo.png) from the application folder and applies it
    // to the brand areas of the window. If the file is absent, the "PC" initials show.
    public static class Branding
    {
        public static void ApplyLogo(Image image, TextBlock initials)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
                if (!File.Exists(path)) return;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 220; // keep small for crispness on tiles
                bitmap.EndInit();
                bitmap.Freeze();

                image.Source = bitmap;
                image.Visibility = Visibility.Visible;
                if (initials != null) initials.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // If the logo can't be loaded for any reason, keep the initials.
            }
        }
    }
}
