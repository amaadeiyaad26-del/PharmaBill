using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;

namespace PharmaBill.App.ViewModels;

public class UserManualViewModel : ObservableObject
{
	private readonly IReadOnlyList<UserManualChapter> _allChapters;

	[ObservableProperty]
	[NotifyPropertyChangedFor("HasChapterSearch")]
	private string _chapterSearchQuery = string.Empty;

	[ObservableProperty]
	private UserManualChapter? _selectedChapter;

	[ObservableProperty]
	private FlowDocument? _document;

	[ObservableProperty]
	private string _statusMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? closeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? printGuideCommand;

	public ObservableCollection<UserManualChapter> FilteredChapters { get; }

	public bool HasChapterSearch => !string.IsNullOrWhiteSpace(ChapterSearchQuery);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ChapterSearchQuery
	{
		get
		{
			return _chapterSearchQuery;
		}
		[MemberNotNull("_chapterSearchQuery")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_chapterSearchQuery, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ChapterSearchQuery);
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HasChapterSearch);
				_chapterSearchQuery = value;
				OnChapterSearchQueryChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ChapterSearchQuery);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HasChapterSearch);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public UserManualChapter? SelectedChapter
	{
		get
		{
			return _selectedChapter;
		}
		set
		{
			if (!EqualityComparer<UserManualChapter>.Default.Equals(_selectedChapter, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedChapter);
				_selectedChapter = value;
				OnSelectedChapterChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedChapter);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public FlowDocument? Document
	{
		get
		{
			return _document;
		}
		set
		{
			if (!EqualityComparer<FlowDocument>.Default.Equals(_document, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Document);
				_document = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Document);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string StatusMessage
	{
		get
		{
			return _statusMessage;
		}
		[MemberNotNull("_statusMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_statusMessage, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StatusMessage);
				_statusMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StatusMessage);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CloseCommand => closeCommand ?? (closeCommand = new RelayCommand(Close));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PrintGuideCommand => printGuideCommand ?? (printGuideCommand = new RelayCommand(PrintGuide));

	public event EventHandler? CloseRequested;

	public UserManualViewModel()
	{
		_allChapters = UserManualContent.Chapters;
		FilteredChapters = new ObservableCollection<UserManualChapter>(_allChapters);
		SelectedChapter = FilteredChapters.FirstOrDefault();
		RebuildDocument();
	}

	private void ApplyChapterFilter(string? raw)
	{
		string[] array = (raw ?? string.Empty).Trim().Split(new char[3] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		FilteredChapters.Clear();
		foreach (UserManualChapter chapter in _allChapters)
		{
			if (array.Length == 0 || array.All((string token) => chapter.Title.Contains(token, StringComparison.OrdinalIgnoreCase) || chapter.Keywords.Contains(token, StringComparison.OrdinalIgnoreCase) || chapter.Id.Contains(token, StringComparison.OrdinalIgnoreCase)))
			{
				FilteredChapters.Add(chapter);
			}
		}
		if ((object)SelectedChapter == null || !FilteredChapters.Contains(SelectedChapter))
		{
			SelectedChapter = FilteredChapters.FirstOrDefault();
		}
	}

	private void RebuildDocument()
	{
		FlowDocument flowDocument = new FlowDocument
		{
			FontFamily = new FontFamily("Segoe UI"),
			FontSize = 13.5,
			PagePadding = new Thickness(4.0),
			TextAlignment = TextAlignment.Left
		};
		if ((object)SelectedChapter == null)
		{
			flowDocument.Blocks.Add(new Paragraph(new Run("Select a chapter from the left."))
			{
				Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
			});
			Document = flowDocument;
			return;
		}
		flowDocument.Blocks.Add(new Paragraph(new Run(SelectedChapter.Title))
		{
			FontSize = 22.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		foreach (GuideBlock block in SelectedChapter.Blocks)
		{
			switch (block.Kind)
			{
			case GuideBlockKind.Heading:
				flowDocument.Blocks.Add(new Paragraph(new Run(block.Text))
				{
					FontSize = 16.0,
					FontWeight = FontWeights.SemiBold,
					Margin = new Thickness(0.0, 14.0, 0.0, 6.0)
				});
				break;
			case GuideBlockKind.Paragraph:
				flowDocument.Blocks.Add(new Paragraph(new Run(block.Text))
				{
					Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
					LineHeight = 22.0
				});
				break;
			case GuideBlockKind.Bullet:
			{
				List list = new List
				{
					MarkerStyle = TextMarkerStyle.Disc,
					Margin = new Thickness(8.0, 0.0, 0.0, 4.0)
				};
				list.ListItems.Add(new ListItem(new Paragraph(new Run(block.Text))
				{
					Margin = new Thickness(0.0, 0.0, 0.0, 2.0)
				}));
				flowDocument.Blocks.Add(list);
				break;
			}
			case GuideBlockKind.Tip:
				flowDocument.Blocks.Add(CreateCallout(block.Text, block.Detail ?? string.Empty, Color.FromRgb(239, 246, byte.MaxValue), Color.FromRgb(37, 99, 235)));
				break;
			case GuideBlockKind.Warning:
				flowDocument.Blocks.Add(CreateCallout(block.Text, block.Detail ?? string.Empty, Color.FromRgb(254, 243, 199), Color.FromRgb(180, 83, 9)));
				break;
			case GuideBlockKind.Shortcut:
				flowDocument.Blocks.Add(CreateShortcutParagraph(block.Text, block.Detail ?? string.Empty));
				break;
			}
		}
		Document = flowDocument;
	}

	private static BlockUIContainer CreateCallout(string title, string body, Color background, Color accent)
	{
		return new BlockUIContainer(new Border
		{
			Background = new SolidColorBrush(background),
			BorderBrush = new SolidColorBrush(accent),
			BorderThickness = new Thickness(3.0, 1.0, 1.0, 1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(12.0, 10.0, 12.0, 10.0),
			Margin = new Thickness(0.0, 6.0, 0.0, 10.0),
			Child = new StackPanel
			{
				Children = 
				{
					(UIElement)new TextBlock
					{
						Text = title,
						FontWeight = FontWeights.SemiBold,
						Foreground = new SolidColorBrush(accent),
						Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
					},
					(UIElement)new TextBlock
					{
						Text = body,
						TextWrapping = TextWrapping.Wrap,
						Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
					}
				}
			}
		});
	}

	private static BlockUIContainer CreateShortcutParagraph(string key, string description)
	{
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		Border element = new Border
		{
			CornerRadius = new CornerRadius(4.0),
			Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
			Padding = new Thickness(8.0, 3.0, 8.0, 3.0),
			Margin = new Thickness(0.0, 0.0, 12.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			Child = new TextBlock
			{
				Text = key,
				FontWeight = FontWeights.SemiBold,
				FontFamily = new FontFamily("Consolas"),
				Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
			}
		};
		DockPanel.SetDock(element, Dock.Left);
		dockPanel.Children.Add(element);
		dockPanel.Children.Add(new TextBlock
		{
			Text = description,
			VerticalAlignment = VerticalAlignment.Center,
			TextWrapping = TextWrapping.Wrap
		});
		return new BlockUIContainer(dockPanel);
	}

	[RelayCommand]
	private void Close()
	{
		CloseRequested?.Invoke(this, EventArgs.Empty);
	}

	[RelayCommand]
	private void PrintGuide()
	{
		if (Document != null)
		{
			FlowDocument flowDocument = CloneDocument(Document);
			PrintDialog printDialog = new PrintDialog();
			if (printDialog.ShowDialog() == true)
			{
				flowDocument.PageHeight = printDialog.PrintableAreaHeight;
				flowDocument.PageWidth = printDialog.PrintableAreaWidth;
				flowDocument.PagePadding = new Thickness(48.0);
				flowDocument.ColumnWidth = double.PositiveInfinity;
				printDialog.PrintDocument(((IDocumentPaginatorSource)flowDocument).DocumentPaginator, "PharmaBill Guide — " + (SelectedChapter?.Title ?? "Manual"));
				StatusMessage = "Guide sent to printer.";
			}
		}
	}

	private static FlowDocument CloneDocument(FlowDocument source)
	{
		TextRange textRange = new TextRange(source.ContentStart, source.ContentEnd);
		using MemoryStream memoryStream = new MemoryStream();
		textRange.Save(memoryStream, DataFormats.Xaml);
		memoryStream.Position = 0L;
		FlowDocument flowDocument = new FlowDocument();
		new TextRange(flowDocument.ContentStart, flowDocument.ContentEnd).Load(memoryStream, DataFormats.Xaml);
		return flowDocument;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnChapterSearchQueryChanged(string value)
	{
		ApplyChapterFilter(value);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedChapterChanged(UserManualChapter? value)
	{
		RebuildDocument();
	}
}
