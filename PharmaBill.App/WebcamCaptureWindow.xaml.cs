using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace PharmaBill.App;

public partial class WebcamCaptureWindow : Window
{
	private sealed record CameraItem(string Id, string Name);

	private const string NoCameraMessage = "No active webcam found. Please connect a USB camera or allow camera access.";

	private const string CameraBlockedMessage = "Camera permission denied or camera in use by another application. Please check Windows Settings > Privacy > Camera.";

	private readonly object _gate = new object();

	private MediaCapture? _capture;

	private MediaFrameReader? _reader;

	private WriteableBitmap? _bitmap;

	private bool _frameInFlight;

	private bool _closed;

	private bool _suppressSelection;

	private int _startVersion;

	private DispatcherTimer? _previewWatchdog;

	public string? CapturedPath { get; private set; }

	public WebcamCaptureWindow(bool prescriptionOcrMode = false)
	{
		InitializeComponent();
		if (prescriptionOcrMode)
		{
			Title = "Extract prescription from webcam";
			CaptureButton.Content = "Capture Frame & Extract";
			OcrGuide.Visibility = Visibility.Visible;
			StatusText.Text = "Hold prescription steady within frame and ensure adequate lighting.";
		}
		Loaded += async (object _, RoutedEventArgs _) =>
		{
			await LoadDevicesAsync();
		};
		Closed += async (object? _, EventArgs _) =>
		{
			_closed = true;
			await StopCameraAsync();
		};
	}

	private async Task LoadDevicesAsync()
	{
		CaptureButton.IsEnabled = false;
		DeviceCombo.IsEnabled = false;
		try
		{
			DeviceInformationCollection deviceInformationCollection = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
			_suppressSelection = true;
			DeviceCombo.ItemsSource = deviceInformationCollection.Select((DeviceInformation d) => new CameraItem(d.Id, string.IsNullOrWhiteSpace(d.Name) ? "Camera" : d.Name)).ToList();
			_suppressSelection = false;
			if (deviceInformationCollection.Count == 0)
			{
				StatusText.Text = "No active webcam found. Please connect a USB camera or allow camera access.";
				return;
			}
			DeviceCombo.IsEnabled = true;
			DeviceCombo.SelectedIndex = 0;
		}
		catch (Exception ex)
		{
			_suppressSelection = false;
			StatusText.Text = "No active webcam found. Please connect a USB camera or allow camera access. (" + ex.Message + ")";
		}
	}

	private async Task StartCameraAsync(CameraItem device)
	{
		int version = ++_startVersion;
		await StopCameraAsync();
		if (_closed || version != _startVersion)
		{
			return;
		}
		CaptureButton.IsEnabled = false;
		StatusText.Text = "Starting " + device.Name + "...";
		MediaCapture capture = null;
		try
		{
			capture = new MediaCapture();
			await capture.InitializeAsync(new MediaCaptureInitializationSettings
			{
				VideoDeviceId = device.Id,
				StreamingCaptureMode = StreamingCaptureMode.Video,
				MemoryPreference = MediaCaptureMemoryPreference.Cpu,
				SharingMode = MediaCaptureSharingMode.ExclusiveControl
			});
			MediaFrameSource mediaFrameSource = capture.FrameSources.Values.FirstOrDefault((MediaFrameSource s) => s.Info.MediaStreamType == MediaStreamType.VideoPreview && s.Info.SourceKind == MediaFrameSourceKind.Color) ?? capture.FrameSources.Values.FirstOrDefault((MediaFrameSource s) => s.Info.SourceKind == MediaFrameSourceKind.Color);
			if ((object)mediaFrameSource == null)
			{
				throw new InvalidOperationException("The camera offers no colour video stream.");
			}
			List<MediaFrameFormat?> formats = RankPreviewFormats(mediaFrameSource);
			Exception? lastError = null;
			foreach (MediaFrameFormat? format in formats)
			{
				MediaFrameReader? reader = null;
				try
				{
					if (format != null)
					{
						await mediaFrameSource.SetFormatAsync(format);
					}
					reader = await capture.CreateFrameReaderAsync(mediaFrameSource, MediaEncodingSubtypes.Bgra8);
					reader.FrameArrived += OnFrameArrived;
					MediaFrameReaderStartStatus mediaFrameReaderStartStatus = await reader.StartAsync();
					if (mediaFrameReaderStartStatus == MediaFrameReaderStartStatus.ExclusiveControlNotAvailable || mediaFrameReaderStartStatus == MediaFrameReaderStartStatus.DeviceNotAvailable)
					{
						throw new UnauthorizedAccessException(CameraBlockedMessage);
					}
					if (mediaFrameReaderStartStatus != MediaFrameReaderStartStatus.Success)
					{
						throw new InvalidOperationException($"The camera stream could not be started ({mediaFrameReaderStartStatus}).");
					}
					if (_closed || version != _startVersion)
					{
						reader.FrameArrived -= OnFrameArrived;
						await reader.StopAsync();
						reader.Dispose();
						capture.Dispose();
						return;
					}
					_capture = capture;
					_reader = reader;
					uint width = format?.VideoFormat?.Width ?? 0;
					uint height = format?.VideoFormat?.Height ?? 0;
					StatusText.Text = width > 0
						? $"Live preview {width}x{height}. Hold the document flat and well lit, then press Capture & Scan."
						: "Hold the prescription flat and well lit, then press Capture & Scan.";
					StartPreviewWatchdog();
					return;
				}
				catch (UnauthorizedAccessException)
				{
					if (reader != null)
					{
						reader.FrameArrived -= OnFrameArrived;
						reader.Dispose();
					}
					throw;
				}
				catch (Exception ex)
				{
					lastError = ex;
					if (reader != null)
					{
						reader.FrameArrived -= OnFrameArrived;
						reader.Dispose();
					}
				}
			}
			throw lastError ?? new InvalidOperationException("No supported camera format could be started.");
		}
		catch (UnauthorizedAccessException)
		{
			capture?.Dispose();
			StatusText.Text = CameraBlockedMessage;
		}
		catch (Exception ex2)
		{
			capture?.Dispose();
			StatusText.Text = IsCameraBlocked(ex2)
				? CameraBlockedMessage
				: "The camera could not be started. It may be busy or unavailable. " + ex2.Message;
		}
	}

	private static List<MediaFrameFormat?> RankPreviewFormats(MediaFrameSource source)
	{
		(uint Width, uint Height)[] preferredSizes = [(1920, 1080), (1280, 720), (640, 480)];
		string[] subtypes = [MediaEncodingSubtypes.Bgra8, MediaEncodingSubtypes.Nv12, MediaEncodingSubtypes.Yuy2, MediaEncodingSubtypes.Rgb32];
		List<MediaFrameFormat> available = source.SupportedFormats.Where(format => format.VideoFormat != null && format.VideoFormat.Width > 0).ToList();
		List<MediaFrameFormat?> ranked = new List<MediaFrameFormat?>();
		foreach ((uint width, uint height) in preferredSizes)
		{
			foreach (string subtype in subtypes)
			{
				ranked.AddRange(available.Where(format => string.Equals(format.Subtype, subtype, StringComparison.OrdinalIgnoreCase) && format.VideoFormat.Width == width && format.VideoFormat.Height == height));
			}
		}
		ranked.AddRange(available.Where(format => subtypes.Contains(format.Subtype, StringComparer.OrdinalIgnoreCase) && format.VideoFormat.Width <= 1920 && format.VideoFormat.Height <= 1080).OrderBy(format => Math.Abs((int)format.VideoFormat.Width - 1280) + Math.Abs((int)format.VideoFormat.Height - 720)));
		ranked.AddRange(available.Where(format => format.VideoFormat.Width <= 1280).OrderBy(format => format.VideoFormat.Width * format.VideoFormat.Height));
		List<MediaFrameFormat?> distinct = ranked.Where(format => format != null).DistinctBy(format => format!.Subtype + ":" + format.VideoFormat.Width + "x" + format.VideoFormat.Height).Cast<MediaFrameFormat?>().Take(8).ToList();
		if (distinct.Count == 0)
		{
			distinct.Add(null);
		}
		return distinct;
	}

	private static bool IsCameraBlocked(Exception exception)
	{
		string message = exception.ToString();
		return message.Contains("denied", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("privacy", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("0x80070005", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("0xC00D3704", StringComparison.OrdinalIgnoreCase);
	}

	private void StartPreviewWatchdog()
	{
		StopPreviewWatchdog();
		_previewWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
		_previewWatchdog.Tick += (_, _) =>
		{
			StopPreviewWatchdog();
			if (!_closed && !CaptureButton.IsEnabled)
			{
				StatusText.Text = CameraBlockedMessage;
			}
		};
		_previewWatchdog.Start();
	}

	private void StopPreviewWatchdog()
	{
		if (_previewWatchdog == null)
		{
			return;
		}
		_previewWatchdog.Stop();
		_previewWatchdog = null;
	}

	private async Task StopCameraAsync()
	{
		StopPreviewWatchdog();
		MediaFrameReader reader;
		MediaCapture capture;
		lock (_gate)
		{
			reader = _reader;
			capture = _capture;
			_reader = null;
			_capture = null;
		}
		if ((object)reader != null)
		{
			reader.FrameArrived -= OnFrameArrived;
			try
			{
				await reader.StopAsync();
			}
			catch (Exception)
			{
			}
			reader.Dispose();
		}
		capture?.Dispose();
	}

	private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
	{
		if (_frameInFlight || _closed)
		{
			return;
		}
		try
		{
			using MediaFrameReference mediaFrameReference = sender.TryAcquireLatestFrame();
			SoftwareBitmap softwareBitmap = mediaFrameReference?.VideoMediaFrame?.SoftwareBitmap;
			if ((object)softwareBitmap == null)
			{
				return;
			}
			using SoftwareBitmap softwareBitmap2 = ((softwareBitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8) ? SoftwareBitmap.Copy(softwareBitmap) : SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8));
			int width = softwareBitmap2.PixelWidth;
			int height = softwareBitmap2.PixelHeight;
			byte[] pixels = new byte[width * height * 4];
			softwareBitmap2.CopyToBuffer(pixels.AsBuffer());
			_frameInFlight = true;
			Dispatcher.InvokeAsync(() =>
			{
				try
				{
					if (!_closed)
					{
						if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
						{
							_bitmap = new WriteableBitmap(width, height, 96.0, 96.0, PixelFormats.Bgra32, null);
							PreviewImage.Source = _bitmap;
						}
						_bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
						if (!CaptureButton.IsEnabled)
						{
							CaptureButton.IsEnabled = true;
							StopPreviewWatchdog();
						}
					}
				}
				finally
				{
					_frameInFlight = false;
				}
			});
		}
		catch (Exception)
		{
			_frameInFlight = false;
		}
	}

	private async void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_suppressSelection && DeviceCombo.SelectedItem is CameraItem device)
		{
			await StartCameraAsync(device);
		}
	}

	private async void Refresh_Click(object sender, RoutedEventArgs e)
	{
		await StopCameraAsync();
		await LoadDevicesAsync();
	}

	private async void Capture_Click(object sender, RoutedEventArgs e)
	{
		if (_bitmap == null)
		{
			return;
		}
		CaptureButton.IsEnabled = false;
		try
		{
			System.Windows.Media.Imaging.BitmapFrame item = System.Windows.Media.Imaging.BitmapFrame.Create(new WriteableBitmap(_bitmap));
			JpegBitmapEncoder jpegBitmapEncoder = new JpegBitmapEncoder
			{
				QualityLevel = 92
			};
			jpegBitmapEncoder.Frames.Add(item);
			using MemoryStream memory = new MemoryStream();
			jpegBitmapEncoder.Save(memory);
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "prescription-captures");
			Directory.CreateDirectory(text);
			string path = Path.Combine(text, $"rx-{DateTime.Now:yyyyMMdd-HHmmss}.jpg");
			await File.WriteAllBytesAsync(path, memory.ToArray());
			CapturedPath = path;
			await StopCameraAsync();
			DialogResult = true;
		}
		catch (Exception ex)
		{
			StatusText.Text = "The photo could not be captured: " + ex.Message;
			CaptureButton.IsEnabled = true;
		}
	}
}
