using System.CodeDom.Compiler;
using System.Collections;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DurationRegex_11 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				while (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan) && runtextpos != inputSpan.Length)
				{
					runtextpos++;
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				if (num <= inputSpan.Length - 2)
				{
					int num2 = inputSpan.Slice(num).IndexOfNonAsciiOrAny_993E894700B1E930D7454E263639B6F6576AC6101B98AE409E33EEFAB3BE1D36();
					if (num2 >= 0)
					{
						runtextpos = num + num2;
						return true;
					}
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
				int capturePosition = 0;
				int capturePosition2 = 0;
				int capturePosition3 = 0;
				int capturePosition4 = 0;
				int arg = 0;
				int arg2 = 0;
				int num7 = 0;
				int num8 = 0;
				int num9 = 0;
				int num10 = 0;
				int num11 = 0;
				int num12 = 0;
				int num13 = 0;
				int num14 = 0;
				int num15 = 0;
				int pos = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				num15 = 0;
				while (true)
				{
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
					num15++;
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPreWordCharBoundary(inputSpan, num) && !span.IsEmpty)
					{
						char c = span[0];
						if ((uint)c <= 88u)
						{
							if (c == 'F')
							{
								goto IL_00c7;
							}
							if (c == 'X')
							{
								goto IL_00b7;
							}
						}
						else
						{
							if (c == 'f')
							{
								goto IL_00c7;
							}
							if (c == 'x')
							{
								goto IL_00b7;
							}
						}
					}
					goto IL_01a8;
					IL_0490:
					UncaptureUntil(capturePosition2);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num9 < num10)
					{
						num = --num10;
						span = inputSpan.Slice(num);
						goto IL_04bd;
					}
					goto IL_04cc;
					IL_04bd:
					capturePosition2 = Crawlpos();
					num2 = 2;
					goto IL_0676;
					IL_0188:
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, arg, arg2, Crawlpos());
					if (num15 == 0)
					{
						continue;
					}
					goto IL_0207;
					IL_03f7:
					num = num4;
					span = inputSpan.Slice(num);
					UncaptureUntil(num3);
					char c2;
					if ((uint)span.Length >= 4u && span.StartsWith("wee", StringComparison.OrdinalIgnoreCase) && !((((c2 = span[3]) | 0x20) != 107) & (c2 != 'K')))
					{
						num += 4;
						span = inputSpan.Slice(num);
						num9 = num;
						if (!span.IsEmpty && (span[0] | 0x20) == 115)
						{
							span = span.Slice(1);
							num++;
						}
						num10 = num;
						goto IL_04bd;
					}
					goto IL_04cc;
					IL_03b4:
					num = num4;
					span = inputSpan.Slice(num);
					UncaptureUntil(num3);
					if (span.IsEmpty || (span[0] | 0x20) != 100)
					{
						goto IL_03f7;
					}
					num2 = 1;
					num++;
					span = inputSpan.Slice(num);
					goto IL_0676;
					IL_0676:
					while (true)
					{
						Capture(2, num6, num);
						if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPostWordCharBoundary(inputSpan, num))
						{
							if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
							{
								CheckTimeout();
							}
							switch (num2)
							{
							case 0:
								break;
							case 1:
								goto IL_03f7;
							case 2:
								goto IL_0490;
							case 3:
								goto IL_0561;
							case 4:
								goto IL_0613;
							default:
								continue;
							}
							break;
						}
						runtextpos = num;
						Capture(0, start, num);
						return true;
					}
					UncaptureUntil(capturePosition);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num7 < num8)
					{
						num = --num8;
						span = inputSpan.Slice(num);
						goto IL_03a5;
					}
					goto IL_03b4;
					IL_00b7:
					num++;
					span = inputSpan.Slice(num);
					goto IL_00ff;
					IL_00c7:
					if ((uint)span.Length >= 3u && span.Slice(1).StartsWith("or", StringComparison.OrdinalIgnoreCase))
					{
						num += 3;
						span = inputSpan.Slice(num);
						goto IL_00ff;
					}
					goto IL_01a8;
					IL_0613:
					UncaptureUntil(capturePosition4);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num13 >= num14)
					{
						goto IL_01ea;
					}
					num = --num14;
					span = inputSpan.Slice(num);
					goto IL_0643;
					IL_059d:
					num = num4;
					span = inputSpan.Slice(num);
					UncaptureUntil(num3);
					if ((uint)span.Length < 5u || !span.StartsWith("month", StringComparison.OrdinalIgnoreCase))
					{
						goto IL_01ea;
					}
					num += 5;
					span = inputSpan.Slice(num);
					num13 = num;
					if (!span.IsEmpty && (span[0] | 0x20) == 115)
					{
						span = span.Slice(1);
						num++;
					}
					num14 = num;
					goto IL_0643;
					IL_00ff:
					arg = num;
					int i;
					for (i = 0; (uint)i < (uint)span.Length && char.IsWhiteSpace(span[i]); i++)
					{
					}
					span = span.Slice(i);
					num += i;
					arg2 = num;
					goto IL_0188;
					IL_058e:
					capturePosition3 = Crawlpos();
					num2 = 3;
					goto IL_0676;
					IL_01ea:
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num15 == 0)
					{
						break;
					}
					UncaptureUntil(runstack[--pos]);
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPop(runstack, ref pos, out arg2, out arg);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (arg < arg2)
					{
						num = --arg2;
						span = inputSpan.Slice(num);
						goto IL_0188;
					}
					goto IL_01a8;
					IL_01a8:
					if (--num15 < 0)
					{
						UncaptureUntil(0);
						return false;
					}
					num = runstack[--pos];
					UncaptureUntil(runstack[--pos]);
					span = inputSpan.Slice(num);
					goto IL_0207;
					IL_03a5:
					capturePosition = Crawlpos();
					num2 = 0;
					goto IL_0676;
					IL_0561:
					UncaptureUntil(capturePosition3);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num11 < num12)
					{
						num = --num12;
						span = inputSpan.Slice(num);
						goto IL_058e;
					}
					goto IL_059d;
					IL_0207:
					span = inputSpan.Slice(num);
					int num16 = num;
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if ((uint)(num - 1) < inputSpan.Length && !(((c2 = inputSpan[num - 1]) < '\u0080') ? (("\0\0怀Ͽ\0\0\0\0"[(int)c2 >> 4] & (1 << (c2 & 0xF))) == 0) : (!RegexRunner.CharInClass(c2, "\0\u0002\u0001-/\t"))))
					{
						num--;
					}
					else
					{
						num = num16;
						span = inputSpan.Slice(num);
						num5 = num;
						int j;
						for (j = 0; j < 3 && (uint)j < (uint)span.Length && char.IsDigit(span[j]); j++)
						{
						}
						if (j != 0)
						{
							span = span.Slice(j);
							num += j;
							Capture(1, num5, num);
							int k;
							for (k = 0; (uint)k < (uint)span.Length && char.IsWhiteSpace(span[k]); k++)
							{
							}
							span = span.Slice(k);
							num += k;
							num6 = num;
							num4 = num;
							num3 = Crawlpos();
							if ((uint)span.Length >= 3u && span.StartsWith("day", StringComparison.OrdinalIgnoreCase))
							{
								num += 3;
								span = inputSpan.Slice(num);
								num7 = num;
								if (!span.IsEmpty && (span[0] | 0x20) == 115)
								{
									span = span.Slice(1);
									num++;
								}
								num8 = num;
								goto IL_03a5;
							}
							goto IL_03b4;
						}
					}
					goto IL_01ea;
					IL_0643:
					capturePosition4 = Crawlpos();
					num2 = 4;
					goto IL_0676;
					IL_04cc:
					num = num4;
					span = inputSpan.Slice(num);
					UncaptureUntil(num3);
					if ((uint)span.Length >= 2u && (span[0] | 0x20) == 119 && !((((c2 = span[1]) | 0x20) != 107) & (c2 != 'K')))
					{
						num += 2;
						span = inputSpan.Slice(num);
						num11 = num;
						if (!span.IsEmpty && (span[0] | 0x20) == 115)
						{
							span = span.Slice(1);
							num++;
						}
						num12 = num;
						goto IL_058e;
					}
					goto IL_059d;
				}
				UncaptureUntil(0);
				return false;
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				void UncaptureUntil(int num17)
				{
					while (Crawlpos() > num17)
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

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DurationRegex_11 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DurationRegex_11();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DurationRegex_11()
	{
		pattern = "(?:\\b(?:x|for)\\s*)?(?<![\\d.\\-])(?<n>\\d{1,3})\\s*(?<unit>days?|d|weeks?|wks?|months?)\\b";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		CapNames = new Hashtable
		{
			{ "0", 0 },
			{ "n", 1 },
			{ "unit", 2 }
		};
		capslist = new string[3] { "0", "n", "unit" };
		capsize = 3;
	}
}
