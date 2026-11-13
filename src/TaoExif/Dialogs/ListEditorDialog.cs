using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaoExif.Dialogs;

/// <summary>A two-field row edited by <see cref="ListEditorDialog"/>.</summary>
public sealed partial class EditPair : ObservableObject
{
    [ObservableProperty]
    private string _first = "";

    [ObservableProperty]
    private string _second = "";
}

/// <summary>Edits an ordered list of (name, tag) pairs: file list columns, workspace rows, command presets.</summary>
public sealed class ListEditorDialog : DialogWindow
{
    private readonly DataGrid _grid;

    public ListEditorDialog(string title, string help, string firstHeader, string secondHeader, IEnumerable<(string, string)> items, Func<IEnumerable<(string, string)>>? defaults = null)
        : base(title, 640)
    {
        Items = new ObservableCollection<EditPair>(items.Select(i => new EditPair { First = i.Item1, Second = i.Item2 }));
        _grid = new DataGrid
        {
            ItemsSource = Items,
            Height = 340,
            CanUserSortColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            AutoGenerateColumns = false,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = firstHeader, Binding = new Binding(nameof(EditPair.First)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = secondHeader, Binding = new Binding(nameof(EditPair.Second)), Width = new DataGridLength(1.6, DataGridLengthUnitType.Star) });

        var add = new Button { Content = "Add" };
        add.Click += (_, _) =>
        {
            var p = new EditPair { First = "New", Second = "" };
            int at = _grid.SelectedIndex >= 0 ? _grid.SelectedIndex + 1 : Items.Count;
            Items.Insert(at, p);
            _grid.SelectedItem = p;
            _grid.ScrollIntoView(p, null);
        };
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) =>
        {
            if (_grid.SelectedItem is EditPair p)
                Items.Remove(p);
        };
        var up = new Button { Content = "Up" };
        up.Click += (_, _) => Move(-1);
        var down = new Button { Content = "Down" };
        down.Click += (_, _) => Move(1);
        var tools = Row(6, add, remove, up, down);
        if (defaults != null)
        {
            var reset = new Button { Content = "Defaults" };
            reset.Click += (_, _) =>
            {
                Items.Clear();
                foreach (var (a, b) in defaults())
                    Items.Add(new EditPair { First = a, Second = b });
            };
            tools.Children.Add(reset);
        }

        SetBody(Column(10, Label(help, true), _grid, tools), "Save");
        Width = 640;
        _grid.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public ObservableCollection<EditPair> Items { get; }

    public List<(string First, string Second)> Result =>
        Items.Where(i => !string.IsNullOrWhiteSpace(i.Second)).Select(i => (i.First.Trim(), i.Second.Trim())).ToList();

    private void Move(int delta)
    {
        if (_grid.SelectedItem is not EditPair p)
            return;
        int i = Items.IndexOf(p), j = i + delta;
        if (j < 0 || j >= Items.Count)
            return;
        Items.Move(i, j);
        _grid.SelectedItem = p;
    }
}
