using System;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.App.Services;

public static class SoundHelper
{
	private static readonly long HoverThrottleMs = 70L;

	private static SoundPlayer? _tickPlayer;

	private static long _lastHoverTickMs;

	public static void Initialize()
	{
		string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tick.wav");
		if (File.Exists(text))
		{
			_tickPlayer = new SoundPlayer(text);
			_tickPlayer.LoadAsync();
		}
	}

	public static void PlayTick()
	{
		Task.Run(() =>
		{
			try
			{
				if (_tickPlayer != null)
				{
					_tickPlayer.Play();
				}
				else
				{
					SystemSounds.Asterisk.Play();
				}
			}
			catch
			{
			}
		});
	}

	public static void PlayHoverTick()
	{
		long tickCount = Environment.TickCount64;
		long num = Interlocked.Read(in _lastHoverTickMs);
		if (tickCount - num < HoverThrottleMs || Interlocked.CompareExchange(ref _lastHoverTickMs, tickCount, num) != num)
		{
			return;
		}
		Task.Run(() =>
		{
			try
			{
				if (_tickPlayer != null)
				{
					_tickPlayer.Play();
				}
				else
				{
					SystemSounds.Asterisk.Play();
				}
			}
			catch
			{
			}
		});
	}
}
