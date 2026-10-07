using System.CodeDom.Compiler;
using System.Collections;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__PatientRegex_5 : Regex
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
				if (num <= inputSpan.Length - 3)
				{
					int num2 = inputSpan.Slice(num).IndexOfAny(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_indexOfAnyStrings_OrdinalIgnoreCase_BAB603892FCAD558374C9BEC405360D05277BA19FBF0D0413CC6FFE09B2E7F49);
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
				int capturePosition = 0;
				int num5 = 0;
				int num6 = 0;
				int num7 = 0;
				int capturePosition2 = 0;
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
				int num18 = 0;
				int pos = 0;
				int num19 = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPreWordCharBoundary(inputSpan, num))
				{
					UncaptureUntil(0);
					return false;
				}
				num5 = num;
				num4 = Crawlpos();
				if (!span.IsEmpty && (span[0] | 0x20) == 112)
				{
					num6 = num;
					capturePosition = Crawlpos();
					if ((uint)span.Length >= 3u && span.Slice(1).StartsWith("at", StringComparison.OrdinalIgnoreCase))
					{
						if ((uint)span.Length > 3u && (span[3] | 0x20) == 105)
						{
							span = span.Slice(1);
							num++;
						}
						if ((uint)span.Length >= 4u && (span[3] | 0x20) == 101)
						{
							if ((uint)span.Length > 4u && (span[4] | 0x20) == 110)
							{
								span = span.Slice(1);
								num++;
							}
							if ((uint)span.Length >= 5u && (span[4] | 0x20) == 116)
							{
								num3 = 0;
								num += 5;
								span = inputSpan.Slice(num);
								goto IL_02fb;
							}
						}
					}
					goto IL_015c;
				}
				goto IL_04bb;
				IL_015c:
				num = num6;
				span = inputSpan.Slice(num);
				UncaptureUntil(capturePosition);
				if ((uint)span.Length >= 4u && span.Slice(1).StartsWith("ati", StringComparison.OrdinalIgnoreCase))
				{
					if ((uint)span.Length > 4u && (span[4] | 0x20) == 110)
					{
						span = span.Slice(1);
						num++;
					}
					if ((uint)span.Length >= 6u && span.Slice(4).StartsWith("et", StringComparison.OrdinalIgnoreCase))
					{
						num3 = 1;
						num += 6;
						span = inputSpan.Slice(num);
						goto IL_02fb;
					}
				}
				goto IL_01f7;
				IL_01f7:
				num = num6;
				span = inputSpan.Slice(num);
				UncaptureUntil(capturePosition);
				if ((uint)span.Length >= 4u && span.Slice(1).StartsWith("ate", StringComparison.OrdinalIgnoreCase))
				{
					if ((uint)span.Length > 4u && (span[4] | 0x20) == 105)
					{
						span = span.Slice(1);
						num++;
					}
					if ((uint)span.Length >= 6u && span.Slice(4).StartsWith("nt", StringComparison.OrdinalIgnoreCase))
					{
						num3 = 2;
						num += 6;
						span = inputSpan.Slice(num);
						goto IL_02fb;
					}
				}
				goto IL_028f;
				IL_03e6:
				capturePosition2 = Crawlpos();
				num17 = 0;
				while (true)
				{
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
					num17++;
					int num20 = span.IndexOfAnyExcept('\t', ' ');
					if (num20 < 0)
					{
						num20 = span.Length;
					}
					if (num20 == 0)
					{
						break;
					}
					span = span.Slice(num20);
					num += num20;
					if ((uint)span.Length < 4u || !span.StartsWith("name", StringComparison.OrdinalIgnoreCase) || !_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPostWordCharBoundary(inputSpan, num + 4))
					{
						break;
					}
					num += 4;
					span = inputSpan.Slice(num);
					if (num17 == 0)
					{
						continue;
					}
					goto IL_04b4;
				}
				goto IL_047a;
				IL_02fb:
				num16 = 0;
				while (true)
				{
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
					num16++;
					if ((uint)span.Length < 2u || !span.StartsWith("'s", StringComparison.OrdinalIgnoreCase))
					{
						break;
					}
					num += 2;
					span = inputSpan.Slice(num);
					if (num16 == 0)
					{
						continue;
					}
					goto IL_0381;
				}
				goto IL_034a;
				IL_04bb:
				num = num5;
				span = inputSpan.Slice(num);
				UncaptureUntil(num4);
				if ((uint)span.Length < 4u || !span.StartsWith("name", StringComparison.OrdinalIgnoreCase))
				{
					UncaptureUntil(0);
					return false;
				}
				if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPostWordCharBoundary(inputSpan, num + 4))
				{
					UncaptureUntil(0);
					return false;
				}
				num2 = 1;
				num += 4;
				span = inputSpan.Slice(num);
				goto IL_053d;
				IL_034a:
				if (--num16 < 0)
				{
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					switch (num3)
					{
					case 0:
						break;
					case 1:
						goto IL_01f7;
					case 2:
						goto IL_028f;
					default:
						goto IL_02fb;
					case 3:
						goto IL_04bb;
					}
					goto IL_015c;
				}
				num = runstack[--pos];
				UncaptureUntil(runstack[--pos]);
				span = inputSpan.Slice(num);
				goto IL_0381;
				IL_0381:
				if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsBoundary(inputSpan, num))
				{
					goto IL_034a;
				}
				num10 = num;
				if (!span.IsEmpty && span[0] == '.')
				{
					span = span.Slice(1);
					num++;
				}
				num11 = num;
				goto IL_03e6;
				IL_04b4:
				num2 = 0;
				goto IL_053d;
				IL_047a:
				if (--num17 < 0)
				{
					UncaptureUntil(capturePosition2);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num10 >= num11)
					{
						goto IL_034a;
					}
					num = --num11;
					span = inputSpan.Slice(num);
					goto IL_03e6;
				}
				num = runstack[--pos];
				UncaptureUntil(runstack[--pos]);
				span = inputSpan.Slice(num);
				goto IL_04b4;
				IL_028f:
				num = num6;
				span = inputSpan.Slice(num);
				UncaptureUntil(capturePosition);
				if ((uint)span.Length >= 2u && (span[1] | 0x20) == 116)
				{
					num3 = 3;
					num += 2;
					span = inputSpan.Slice(num);
					goto IL_02fb;
				}
				goto IL_04bb;
				IL_053d:
				while (true)
				{
					num12 = num;
					int num21 = span.IndexOfAnyExcept('\t', ' ');
					if (num21 < 0)
					{
						num21 = span.Length;
					}
					span = span.Slice(num21);
					num += num21;
					num13 = num;
					while (true)
					{
						num8 = Crawlpos();
						int num22 = span.IndexOfAnyExcept('-', '.', ':');
						if (num22 < 0)
						{
							num22 = span.Length;
						}
						span = span.Slice(num22);
						num += num22;
						int num23 = span.IndexOfAnyExcept('\t', ' ');
						if (num23 < 0)
						{
							num23 = span.Length;
						}
						span = span.Slice(num23);
						num += num23;
						num7 = num;
						char c;
						if (!span.IsEmpty && !(((c = span[0]) < '\u0080') ? (!char.IsAsciiLetter(c)) : (!RegexRunner.CharInClass(c, "\0\u0006\0A[a{KÅ"))))
						{
							num++;
							span = inputSpan.Slice(num);
							num14 = num;
							int num24 = span.IndexOfAnyExcept(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_nonAscii_01FE667C4BB729047C81DE888AA0FBC694D1DFE7FE21E6740BEFF0BFCE194DC1);
							if (num24 < 0)
							{
								num24 = span.Length;
							}
							span = span.Slice(num24);
							num += num24;
							num15 = num;
							while (true)
							{
								num9 = Crawlpos();
								num19 = pos;
								num18 = 0;
								while (true)
								{
									_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
									num18++;
									int num25 = span.IndexOfAnyExcept('\t', ' ');
									if (num25 < 0)
									{
										num25 = span.Length;
									}
									if (num25 != 0)
									{
										span = span.Slice(num25);
										num += num25;
										if (!span.IsEmpty && !(((c = span[0]) < '\u0080') ? (!char.IsAsciiLetter(c)) : (!RegexRunner.CharInClass(c, "\0\u0006\0A[a{KÅ"))))
										{
											int num26 = span.Slice(1).IndexOfAnyExcept(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_nonAscii_01FE667C4BB729047C81DE888AA0FBC694D1DFE7FE21E6740BEFF0BFCE194DC1);
											if (num26 < 0)
											{
												num26 = span.Length - 1;
											}
											span = span.Slice(num26);
											num += num26;
											num++;
											span = inputSpan.Slice(num);
											if (num18 < 3)
											{
												continue;
											}
											goto IL_07c5;
										}
									}
									if (--num18 < 0)
									{
										break;
									}
									num = runstack[--pos];
									UncaptureUntil(runstack[--pos]);
									span = inputSpan.Slice(num);
									goto IL_07c5;
									IL_07c5:
									pos = num19;
									Capture(1, num7, num);
									runtextpos = num;
									Capture(0, start, num);
									return true;
								}
								UncaptureUntil(num9);
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
						}
						UncaptureUntil(num8);
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
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num2 == 0)
					{
						break;
					}
					if (num2 == 1)
					{
						UncaptureUntil(0);
						return false;
					}
				}
				goto IL_047a;
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				void UncaptureUntil(int num27)
				{
					while (Crawlpos() > num27)
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

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__PatientRegex_5 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__PatientRegex_5();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__PatientRegex_5()
	{
		pattern = "(?:\\b(?:Pati?en?t|Patin?et|Patei?nt|Pt)(?:'s)?\\b\\.?(?:[ \\t]+name\\b)?|\\bName\\b)[ \\t]*[:\\-.]*[ \\t]*(?<n>[A-Za-z][A-Za-z.'\\-]*(?:[ \\t]+[A-Za-z][A-Za-z.'\\-]*){0,3})";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		CapNames = new Hashtable
		{
			{ "0", 0 },
			{ "n", 1 }
		};
		capslist = new string[2] { "0", "n" };
		capsize = 2;
	}
}
