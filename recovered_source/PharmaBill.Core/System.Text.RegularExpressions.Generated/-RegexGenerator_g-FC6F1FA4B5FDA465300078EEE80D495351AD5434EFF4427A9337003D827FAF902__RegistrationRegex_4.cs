using System.CodeDom.Compiler;
using System.Collections;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__RegistrationRegex_4 : Regex
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
				if (num <= inputSpan.Length - 6)
				{
					int num2 = inputSpan.Slice(num).IndexOfAny(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_indexOfString_reg_OrdinalIgnoreCase);
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
				int num18 = 0;
				int num19 = 0;
				int pos = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPreWordCharBoundary(inputSpan, num))
				{
					UncaptureUntil(0);
					return false;
				}
				if ((uint)span.Length < 3u || !span.StartsWith("reg", StringComparison.OrdinalIgnoreCase))
				{
					UncaptureUntil(0);
					return false;
				}
				num += 3;
				span = inputSpan.Slice(num);
				num18 = 0;
				while (true)
				{
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
					num18++;
					if (!span.IsEmpty)
					{
						switch (span[0])
						{
						case 'D':
						case 'd':
							num++;
							span = inputSpan.Slice(num);
							goto IL_0129;
						case 'I':
						case 'i':
							if ((uint)span.Length < 9u || !span.Slice(1).StartsWith("stration", StringComparison.OrdinalIgnoreCase))
							{
								break;
							}
							num += 9;
							span = inputSpan.Slice(num);
							goto IL_0129;
						}
					}
					goto IL_0132;
					IL_0172:
					num8 = num;
					if (!span.IsEmpty && span[0] == '.')
					{
						span = span.Slice(1);
						num++;
					}
					num9 = num;
					while (true)
					{
						num3 = Crawlpos();
						num10 = num;
						int num20 = span.IndexOfAnyExcept('\t', ' ');
						if (num20 < 0)
						{
							num20 = span.Length;
						}
						span = span.Slice(num20);
						num += num20;
						num11 = num;
						while (true)
						{
							num4 = Crawlpos();
							num19 = 0;
							while (true)
							{
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
								num19++;
								int arg = num;
								int arg2 = Crawlpos();
								if ((uint)span.Length < 2u || !span.StartsWith("no", StringComparison.OrdinalIgnoreCase))
								{
									goto IL_02d1;
								}
								if ((uint)span.Length > 2u && span[2] == '.')
								{
									span = span.Slice(1);
									num++;
								}
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, 0, arg, arg2);
								num += 2;
								span = inputSpan.Slice(num);
								goto IL_03b7;
								IL_02d1:
								num = arg;
								span = inputSpan.Slice(num);
								UncaptureUntil(arg2);
								if ((uint)span.Length < 6u || !span.StartsWith("number", StringComparison.OrdinalIgnoreCase))
								{
									goto IL_0329;
								}
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, 1, arg, arg2);
								num += 6;
								span = inputSpan.Slice(num);
								goto IL_03b7;
								IL_0415:
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
									num5 = Crawlpos();
									num14 = num;
									char c;
									if (!span.IsEmpty && (((c = span[0]) == '-') | (c == ':')))
									{
										span = span.Slice(1);
										num++;
									}
									num15 = num;
									while (true)
									{
										num6 = Crawlpos();
										num16 = num;
										int num22 = span.IndexOfAnyExcept('\t', ' ');
										if (num22 < 0)
										{
											num22 = span.Length;
										}
										span = span.Slice(num22);
										num += num22;
										num17 = num;
										while (true)
										{
											num7 = Crawlpos();
											num2 = num;
											int i;
											for (i = 0; i < 5 && (uint)i < (uint)span.Length; i++)
											{
												if (!(((c = span[i]) < '\u0080') ? char.IsAsciiLetter(c) : RegexRunner.CharInClass(c, "\0\u0006\0A[a{KÅ")))
												{
													break;
												}
											}
											span = span.Slice(i);
											num += i;
											if (!span.IsEmpty && (((c = span[0]) == ' ') | (c == '-') | (c == '/')))
											{
												span = span.Slice(1);
												num++;
											}
											if (!span.IsEmpty && char.IsDigit(span[0]))
											{
												num++;
												span = inputSpan.Slice(num);
												int j;
												for (j = 0; j < 18 && (uint)j < (uint)span.Length; j++)
												{
													if (!(((c = span[j]) < '\u0080') ? (("\0\0ꀀϿ\ufffe߿\ufffe߿"[(int)c >> 4] & (1 << (c & 0xF))) != 0) : RegexRunner.CharInClass(c, "\0\n\0-./:A[a{KÅ")))
													{
														break;
													}
												}
												if (j >= 2)
												{
													span = span.Slice(j);
													num += j;
													Capture(1, num2, num);
													runtextpos = num;
													Capture(0, start, num);
													return true;
												}
											}
											UncaptureUntil(num7);
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
										}
										UncaptureUntil(num6);
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
									UncaptureUntil(num5);
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
								if (num19 != 0)
								{
									if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
									{
										CheckTimeout();
									}
									_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPop(runstack, ref pos, out arg2, out arg);
									switch (runstack[--pos])
									{
									case 0:
										break;
									case 1:
										goto IL_0329;
									default:
										goto IL_03b7;
									case 2:
										goto IL_03c0;
									}
									goto IL_02d1;
								}
								break;
								IL_0329:
								num = arg;
								span = inputSpan.Slice(num);
								UncaptureUntil(arg2);
								if (!span.IsEmpty && span[0] == '#')
								{
									_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, 2, arg, arg2);
									num++;
									span = inputSpan.Slice(num);
									goto IL_03b7;
								}
								goto IL_03c0;
								IL_03b7:
								if (num19 == 0)
								{
									continue;
								}
								goto IL_0415;
								IL_03c0:
								if (--num19 < 0)
								{
									break;
								}
								num = runstack[--pos];
								UncaptureUntil(runstack[--pos]);
								span = inputSpan.Slice(num);
								goto IL_0415;
							}
							UncaptureUntil(num4);
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
						UncaptureUntil(num3);
						if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
						{
							CheckTimeout();
						}
						if (num8 >= num9)
						{
							break;
						}
						num = --num9;
						span = inputSpan.Slice(num);
					}
					goto IL_0132;
					IL_0129:
					if (num18 == 0)
					{
						continue;
					}
					goto IL_0172;
					IL_0132:
					if (--num18 < 0)
					{
						break;
					}
					num = runstack[--pos];
					UncaptureUntil(runstack[--pos]);
					span = inputSpan.Slice(num);
					goto IL_0172;
				}
				UncaptureUntil(0);
				return false;
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

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__RegistrationRegex_4 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__RegistrationRegex_4();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__RegistrationRegex_4()
	{
		pattern = "\\bReg(?:d|istration)?\\.?[ \\t]*(?:No\\.?|Number|#)?[ \\t]*[:\\-]?[ \\t]*(?<r>[A-Z]{0,5}[ \\-/]?\\d[A-Z0-9\\-/]{2,18})";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		CapNames = new Hashtable
		{
			{ "0", 0 },
			{ "r", 1 }
		};
		capslist = new string[2] { "0", "r" };
		capsize = 2;
	}
}
