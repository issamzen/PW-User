using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ProfessionalPowerCopyCatalogModern
{
    public sealed class CatalogItem : INotifyPropertyChanged
    {
        private bool _isFavorite;

        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Inputs { get; set; }
        public string Output { get; set; }
        public string Thumbnail { get; set; }
        public string ThumbnailFullPath { get; set; }
        public string CatPartPath { get; set; }
        public string PowerCopyName { get; set; }
        public string CheckScriptDirectory { get; set; }
        public string CheckScriptFile { get; set; }
        public string CheckFunction { get; set; }

        /// <summary>
        /// Optional workflow marker. "lifter" enables the integrated Lifter Studio
        /// (catvba port) when this template is used with "Use in CATIA". Sourced
        /// from the server catalog metadata, the package manifest or catalog.json.
        /// </summary>
        public string Workflow { get; set; }

        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite == value) return;
                _isFavorite = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class CheckRow
    {
        public string Position { get; set; }
        public string Status { get; set; }
        public string Distance { get; set; }
        public string NearestBody { get; set; }
        public string StatusGlyph { get; set; }
        public Brush StatusBackground { get; set; }
        public Brush StatusForeground { get; set; }
    }
}
