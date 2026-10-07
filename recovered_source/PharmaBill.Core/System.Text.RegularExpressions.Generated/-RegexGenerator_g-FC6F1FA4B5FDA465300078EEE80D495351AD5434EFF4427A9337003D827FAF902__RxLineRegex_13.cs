using System.CodeDom.Compiler;
using System.Collections;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__RxLineRegex_13 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				if (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan))
				{
					runtextpos = inputSpan.Length;
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				if ((uint)num < (uint)inputSpan.Length && num == 0)
				{
					return true;
				}
				runtextpos = inputSpan.Length;
				return false;
			}

			private bool TryMatchAtCurrentPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				int start = num;
				int num2 = 0;
				int num3 = 0;
				int num4 = 0;
				int num5 = 0;
				int num6 = 0;
				int num7 = 0;
				int num8 = 0;
				int num9 = 0;
				int num10 = 0;
				int num11 = 0;
				int num12 = 0;
				int num13 = 0;
				int num14 = 0;
				int num15 = 0;
				int num16 = 0;
				int num17 = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (num != 0)
				{
					UncaptureUntil(0);
					return false;
				}
				int i;
				for (i = 0; (uint)i < (uint)span.Length && char.IsWhiteSpace(span[i]); i++)
				{
				}
				span = span.Slice(i);
				num += i;
				num4 = num;
				num3 = Crawlpos();
				if (!span.IsEmpty && (span[0] | 0x20) == 114)
				{
					if ((uint)span.Length > 1u && span[1] == '/')
					{
						span = span.Slice(1);
						num++;
					}
					if ((uint)span.Length >= 2u && (span[1] | 0x20) == 120)
					{
						num2 = 0;
						num += 2;
						span = inputSpan.Slice(num);
						goto IL_01b5;
					}
				}
				goto IL_00fd;
				IL_013d:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if ((uint)span.Length < 2u || !span.StartsWith("rx", StringComparison.OrdinalIgnoreCase))
				{
					UncaptureUntil(0);
					return false;
				}
				num2 = 2;
				num += 2;
				span = inputSpan.Slice(num);
				goto IL_01b5;
				IL_01b5:
				while (true)
				{
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsBoundary(inputSpan, num))
					{
						num10 = num;
						int num18 = span.IndexOfAnyExcept('\t', ' ');
						if (num18 < 0)
						{
							num18 = span.Length;
						}
						span = span.Slice(num18);
						num += num18;
						num11 = num;
						while (true)
						{
							num6 = Crawlpos();
							num12 = num;
							char c;
							if (!span.IsEmpty && (((c = span[0]) == '-') | (c == '.') | (c == ':')))
							{
								span = span.Slice(1);
								num++;
							}
							num13 = num;
							while (true)
							{
								num7 = Crawlpos();
								num14 = num;
								int num19 = span.IndexOfAnyExcept('\t', ' ');
								if (num19 < 0)
								{
									num19 = span.Length;
								}
								span = span.Slice(num19);
								num += num19;
								num15 = num;
								while (true)
								{
									num8 = Crawlpos();
									num5 = num;
									num16 = num;
									int num20 = span.IndexOf('\n');
									if (num20 < 0)
									{
										num20 = span.Length;
									}
									span = span.Slice(num20);
									num += num20;
									num17 = num;
									while (true)
									{
										num9 = Crawlpos();
										Capture(1, num5, num);
										if (num < inputSpan.Length - 1 || ((uint)num < (uint)inputSpan.Length && inputSpan[num] != '\n'))
										{
											UncaptureUntil(num9);
											if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
											{
												CheckTimeout();
											}
											if (num16 >= num17)
											{
												break;
											}
											num = --num17;
											span = inputSpan.Slice(num);
											continue;
										}
										runtextpos = num;
										Capture(0, start, num);
										return true;
									}
									UncaptureUntil(num8);
									if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
									{
										CheckTimeout();
									}
									if (num14 >= num15)
									{
										break;
									}
									num = --num15;
									span = inputSpan.Slice(num);
								}
								UncaptureUntil(num7);
								if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
								{
									CheckTimeout();
								}
								if (num12 >= num13)
								{
									break;
								}
								num = --num13;
								span = inputSpan.Slice(num);
							}
							UncaptureUntil(num6);
							if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
							{
								CheckTimeout();
							}
							if (num10 >= num11)
							{
								break;
							}
							num = --num11;
							span = inputSpan.Slice(num);
						}
					}
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					switch (num2)
					{
					case 0:
						break;
					case 1:
						goto IL_013d;
					case 2:
						UncaptureUntil(0);
						return false;
					default:
						continue;
					}
					break;
				}
				goto IL_00fd;
				IL_00fd:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if (span.IsEmpty || span[0] != '℞')
				{
					goto IL_013d;
				}
				num2 = 1;
				num++;
				span = inputSpan.Slice(num);
				goto IL_01b5;
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				void UncaptureUntil(int capturePosition)
				{
					while (Crawlpos() > capturePosition)
					{
						Uncapture();
					}
				}
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__RxLineRegex_13 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__RxLineRegex_13();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__RxLineRegex_13()
	{
		pattern = "^\\s*(?:R/?x|℞|Rx)\\b[ \\t]*[:.\\-]?[ \\t]*(?<rest>.*)$";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		CapNames = new Hashtable
		{
			{ "0", 0 },
			{ "rest", 1 }
		};
		capslist = new string[2] { "0", "rest" };
		capsize = 2;
	}
}
