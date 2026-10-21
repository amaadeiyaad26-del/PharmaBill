using PharmaBill.Core;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PharmaBill.App.ViewModels;

/// <summary>Small desk tools: a calendar, a notepad that saves as you type, and standard narrations to copy into bills.</summary>
public sealed class ToolsPageViewModel : ObservableObject, ILoadablePage
{
	private static readonly string ToolsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "tools");

	private static readonly string NotePath = Path.Combine(ToolsFolder, "notepad.txt");

	private static readonly string NarrationsPath = Path.Combine(ToolsFolder, "narrations.json");

	private DateTime _selectedDate = BusinessClock.Now.Date;

	private string _noteText = string.Empty;

	private string _newNarration = string.Empty;

	private string _statusMessage = string.Empty;

	private RelayCommand? addNarrationCommand;

	private RelayCommand<string?>? removeNarrationCommand;

	private RelayCommand<string?>? copyNarrationCommand;

	public ObservableCollection<string> Narrations { get; } = new ObservableCollection<string>();

	public DateTime SelectedDate
	{
		get => _selectedDate;
		set => SetProperty(ref _selectedDate, value);
	}

	public string NoteText
	{
		get => _noteText;
		set
		{
			if (SetProperty(ref _noteText, value))
			{
				SaveNote();
			}
		}
	}

	public string NewNarration
	{
		get => _newNarration;
		set => SetProperty(ref _newNarration, value);
	}

	public string StatusMessage
	{
		get => _statusMessage;
		set => SetProperty(ref _statusMessage, value);
	}

	public IRelayCommand AddNarrationCommand => addNarrationCommand ??= new RelayCommand(AddNarration);

	public IRelayCommand<string?> RemoveNarrationCommand => removeNarrationCommand ??= new RelayCommand<string?>(RemoveNarration);

	public IRelayCommand<string?> CopyNarrationCommand => copyNarrationCommand ??= new RelayCommand<string?>(CopyNarration);

	public Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		Directory.CreateDirectory(ToolsFolder);
		_noteText = File.Exists(NotePath) ? File.ReadAllText(NotePath) : string.Empty;
		OnPropertyChanged(nameof(NoteText));

		Narrations.Clear();
		if (File.Exists(NarrationsPath))
		{
			string[] saved = JsonSerializer.Deserialize<string[]>(File.ReadAllText(NarrationsPath)) ?? Array.Empty<string>();
			foreach (string narration in saved)
			{
				Narrations.Add(narration);
			}
		}
		return Task.CompletedTask;
	}

	private void SaveNote()
	{
		try
		{
			Directory.CreateDirectory(ToolsFolder);
			File.WriteAllText(NotePath, _noteText);
		}
		catch (IOException ex)
		{
			StatusMessage = "Notepad could not be saved: " + ex.Message;
		}
	}

	private void AddNarration()
	{
		string text = NewNarration.Trim();
		if (text.Length == 0)
		{
			StatusMessage = "Type the narration first.";
			return;
		}
		if (Narrations.Any(item => string.Equals(item, text, StringComparison.OrdinalIgnoreCase)))
		{
			StatusMessage = "That narration is already in the list.";
			return;
		}

		Narrations.Add(text);
		NewNarration = string.Empty;
		SaveNarrations();
		StatusMessage = "Narration added.";
	}

	private void RemoveNarration(string? narration)
	{
		if (narration != null && Narrations.Remove(narration))
		{
			SaveNarrations();
			StatusMessage = "Narration removed.";
		}
	}

	private void CopyNarration(string? narration)
	{
		if (string.IsNullOrEmpty(narration))
		{
			return;
		}

		Clipboard.SetText(narration);
		StatusMessage = "Copied. Paste it into the narration or remarks box.";
	}

	private void SaveNarrations()
	{
		try
		{
			Directory.CreateDirectory(ToolsFolder);
			File.WriteAllText(NarrationsPath, JsonSerializer.Serialize(Narrations.ToArray()));
		}
		catch (IOException ex)
		{
			StatusMessage = "Narrations could not be saved: " + ex.Message;
		}
	}
}
