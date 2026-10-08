using System;
using System.IO;
using System.Media;
using System.Threading;

namespace PharmaBill.App.Services;

/// <summary>
/// Cached UI click player. Soft mechanical tik; respects EnableUiSounds preference.
/// </summary>
public static class SoundHelper
{
	private const long HoverThrottleMs = 140L;

	private static readonly object Gate = new();
	private static SoundPlayer? _clickPlayer;
	private static bool _enabled = true;
	private static bool _initialized;
	private static long _lastHoverTickMs;
	private static int _playGate;

	public static bool IsEnabled => Volatile.Read(ref _enabled);

	public static void Initialize()
	{
		lock (Gate)
		{
			if (_initialized)
			{
				return;
			}

			try
			{
				string prefsRoot = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					"PharmaBill");
				_enabled = new UiSoundPreferencesStore(prefsRoot).Load().EnableUiSounds;
			}
			catch
			{
				_enabled = true;
			}

			TryLoadPlayer();
			_initialized = true;
		}
	}

	public static void SetEnabled(bool enabled)
	{
		Volatile.Write(ref _enabled, enabled);
		if (!enabled)
		{
			try
			{
				_clickPlayer?.Stop();
			}
			catch
			{
			}
		}
	}

	public static void PlayTick()
	{
		PlayInternal(isHover: false);
	}

	public static void PlayHoverTick()
	{
		long now = Environment.TickCount64;
		long previous = Interlocked.Read(in _lastHoverTickMs);
		if (now - previous < HoverThrottleMs || Interlocked.CompareExchange(ref _lastHoverTickMs, now, previous) != previous)
		{
			return;
		}

		PlayInternal(isHover: true);
	}

	private static void PlayInternal(bool isHover)
	{
		if (!Volatile.Read(ref _enabled))
		{
			return;
		}

		// Serialize plays so rapid hover cannot queue SoundPlayer instances.
		if (Interlocked.CompareExchange(ref _playGate, 1, 0) != 0)
		{
			return;
		}

		ThreadPool.QueueUserWorkItem(_ =>
		{
			try
			{
				SoundPlayer? player;
				lock (Gate)
				{
					if (!_initialized)
					{
						Initialize();
					}

					player = _clickPlayer;
					if (player == null)
					{
						TryLoadPlayer();
						player = _clickPlayer;
					}
				}

				if (!Volatile.Read(ref _enabled))
				{
					return;
				}

				if (player != null)
				{
					player.Play();
				}
				else if (!isHover)
				{
					// Soft system fallback only for intentional clicks, never for hover spam.
					SystemSounds.Beep.Play();
				}
			}
			catch
			{
			}
			finally
			{
				Interlocked.Exchange(ref _playGate, 0);
			}
		});
	}

	private static void TryLoadPlayer()
	{
		try
		{
			string baseDir = AppDomain.CurrentDomain.BaseDirectory;
			string[] candidates =
			{
				Path.Combine(baseDir, "Assets", "ui-click.wav"),
				Path.Combine(baseDir, "Assets", "tick.wav")
			};

			foreach (string path in candidates)
			{
				if (!File.Exists(path))
				{
					continue;
				}

				SoundPlayer player = new SoundPlayer(path);
				player.Load(); // sync load once into memory buffer
				_clickPlayer = player;
				return;
			}
		}
		catch
		{
			_clickPlayer = null;
		}
	}
}