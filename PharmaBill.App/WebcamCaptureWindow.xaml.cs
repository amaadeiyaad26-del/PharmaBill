using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

	private readonly object _gate = new object();

	private MediaCapture? _capture;

	private MediaFrameReader? _reader;

	private WriteableBitmap? _bitmap;

	private bool _frameInFlight;

	private bool _closed;

	private bool _suppressSelection;

	private int _startVersion;

	public string? CapturedPath { get; private set; }

	public WebcamCaptureWindow()
	{
		InitializeComponent();
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
				SharingMode = MediaCaptureSharingMode.SharedReadOnly
			});
			MediaFrameSource mediaFrameSource = capture.FrameSources.Values.FirstOrDefault((MediaFrameSource s) => s.Info.MediaStreamType == MediaStreamType.VideoPreview && s.Info.SourceKind == MediaFrameSourceKind.Color) ?? capture.FrameSources.Values.FirstOrDefault((MediaFrameSource s) => s.Info.SourceKind == MediaFrameSourceKind.Color);
			if ((object)mediaFrameSource == null)
			{
				throw new InvalidOperationException("The camera offers no colour video stream.");
			}
			MediaFrameReader reader = await capture.CreateFrameReaderAsync(mediaFrameSource, MediaEncodingSubtypes.Bgra8);
			reader.FrameArrived += OnFrameArrived;
			MediaFrameReaderStartStatus mediaFrameReaderStartStatus = await reader.StartAsync();
			if (mediaFrameReaderStartStatus != MediaFrameReaderStartStatus.Success)
			{
				reader.FrameArrived -= OnFrameArrived;
				reader.Dispose();
				throw new InvalidOperationException((mediaFrameReaderStartStatus == MediaFrameReaderStartStatus.ExclusiveControlNotAvailable) ? "The camera is in use by another application." : $"The camera stream could not be started ({mediaFrameReaderStartStatus}).");
			}
			if (_closed || version != _startVersion)
			{
				reader.FrameArrived -= OnFrameArrived;
				await reader.StopAsync();
				reader.Dispose();
				capture.Dispose();
			}
			else
			{
				_capture = capture;
				_reader = reader;
				StatusText.Text = "Hold the prescription flat and well lit, then press Capture & Scan.";
			}
		}
		catch (UnauthorizedAccessException)
		{
			capture?.Dispose();
			StatusText.Text = "Camera access is blocked. Allow desktop apps to use the camera in Windows Settings > Privacy > Camera, or close any app using the webcam.";
		}
		catch (Exception ex2)
		{
			capture?.Dispose();
			StatusText.Text = "The camera could not be started. It may be busy or unavailable. " + ex2.Message;
		}
	}

	private async Task StopCameraAsync()
	{
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
