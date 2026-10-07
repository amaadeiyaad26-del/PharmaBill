using System.CodeDom.Compiler;
using System.Collections;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__FormPrefixRegex_8 : Regex
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
				if (num <= inputSpan.Length - 4 && num == 0)
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
				int capturePosition = 0;
				int capturePosition2 = 0;
				int capturePosition3 = 0;
				int num7 = 0;
				int num8 = 0;
				int num9 = 0;
				int arg = 0;
				int arg2 = 0;
				int arg3 = 0;
				int arg4 = 0;
				int arg5 = 0;
				int arg6 = 0;
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
				int num20 = 0;
				int num21 = 0;
				int num22 = 0;
				int num23 = 0;
				int num24 = 0;
				int num25 = 0;
				int num26 = 0;
				int num27 = 0;
				int num28 = 0;
				int pos = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (num != 0)
				{
					UncaptureUntil(0);
					return false;
				}
				num22 = 0;
				while (true)
				{
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
					num22++;
					int i;
					for (i = 0; i < 2 && (uint)i < (uint)span.Length && char.IsDigit(span[i]); i++)
					{
					}
					if (i != 0)
					{
						span = span.Slice(i);
						num += i;
						int j;
						for (j = 0; (uint)j < (uint)span.Length && char.IsWhiteSpace(span[j]); j++)
						{
						}
						span = span.Slice(j);
						num += j;
						char c;
						if (!span.IsEmpty && !((((c = span[0]) | 4) != 45) & (c != '.')))
						{
							num++;
							span = inputSpan.Slice(num);
							arg = num;
							int k;
							for (k = 0; (uint)k < (uint)span.Length && char.IsWhiteSpace(span[k]); k++)
							{
							}
							span = span.Slice(k);
							num += k;
							arg2 = num;
							goto IL_01e9;
						}
					}
					goto IL_0209;
					IL_0209:
					if (--num22 < 0)
					{
						UncaptureUntil(0);
						return false;
					}
					num = runstack[--pos];
					UncaptureUntil(runstack[--pos]);
					span = inputSpan.Slice(num);
					goto IL_0268;
					IL_0268:
					num23 = 0;
					while (true)
					{
						_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
						num23++;
						if ((uint)span.Length >= 2u && span.StartsWith("rx", StringComparison.OrdinalIgnoreCase))
						{
							num += 2;
							span = inputSpan.Slice(num);
							arg3 = num;
							int l;
							for (l = 0; (uint)l < (uint)span.Length && char.IsWhiteSpace(span[l]); l++)
							{
							}
							span = span.Slice(l);
							num += l;
							arg4 = num;
							goto IL_0343;
						}
						goto IL_0434;
						IL_09a7:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length < 3u || !span.StartsWith("gel", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_09ef;
						}
						num2 = 6;
						num += 3;
						span = inputSpan.Slice(num);
						goto IL_0d2d;
						IL_0d2d:
						while (true)
						{
							Capture(1, num5, num);
							if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPostWordCharBoundary(inputSpan, num))
							{
								num16 = num;
								if (!span.IsEmpty && span[0] == '.')
								{
									span = span.Slice(1);
									num++;
								}
								num17 = num;
								while (true)
								{
									num7 = Crawlpos();
									num18 = num;
									int m;
									for (m = 0; (uint)m < (uint)span.Length && char.IsWhiteSpace(span[m]); m++)
									{
									}
									span = span.Slice(m);
									num += m;
									num19 = num;
									while (true)
									{
										num8 = Crawlpos();
										num6 = num;
										num20 = num;
										int num29 = span.IndexOf('\n');
										if (num29 < 0)
										{
											num29 = span.Length;
										}
										if (num29 != 0)
										{
											span = span.Slice(num29);
											num += num29;
											num21 = num;
											num20++;
											while (true)
											{
												num9 = Crawlpos();
												Capture(2, num6, num);
												if (num < inputSpan.Length - 1 || ((uint)num < (uint)inputSpan.Length && inputSpan[num] != '\n'))
												{
													UncaptureUntil(num9);
													if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
													{
														CheckTimeout();
													}
													if (num20 >= num21)
													{
														break;
													}
													num = --num21;
													span = inputSpan.Slice(num);
													continue;
												}
												runtextpos = num;
												Capture(0, start, num);
												return true;
											}
										}
										UncaptureUntil(num8);
										if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
										{
											CheckTimeout();
										}
										if (num18 >= num19 || (num19 = inputSpan.Slice(num18, num19 - num18).LastIndexOfAnyExcept('\n')) < 0)
										{
											break;
										}
										num19 += num18;
										num = num19;
										span = inputSpan.Slice(num);
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
							}
							if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
							{
								CheckTimeout();
							}
							switch (num2)
							{
							case 13:
								break;
							case 0:
								goto IL_0581;
							case 1:
								goto IL_06bf;
							case 2:
								goto IL_07b7;
							case 3:
								goto IL_084d;
							case 4:
								goto IL_0921;
							case 5:
								goto IL_09a7;
							case 6:
								goto IL_09ef;
							case 7:
								goto IL_0a62;
							case 8:
								goto IL_0b34;
							case 9:
								goto IL_0bbb;
							case 10:
								goto IL_0c04;
							case 11:
								goto IL_0c4d;
							case 12:
								goto IL_0c96;
							default:
								continue;
							}
							break;
						}
						goto IL_0470;
						IL_0b34:
						if (--num28 >= 0)
						{
							num = runstack[--pos];
							UncaptureUntil(runstack[--pos]);
							span = inputSpan.Slice(num);
							goto IL_0b6b;
						}
						goto IL_0b72;
						IL_09ef:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length >= 4u && span.StartsWith("drop", StringComparison.OrdinalIgnoreCase))
						{
							num += 4;
							span = inputSpan.Slice(num);
							num14 = num;
							if (!span.IsEmpty && (span[0] | 0x20) == 115)
							{
								span = span.Slice(1);
								num++;
							}
							num15 = num;
							goto IL_0a8f;
						}
						goto IL_0a9e;
						IL_0434:
						if (--num23 < 0)
						{
							break;
						}
						num = runstack[--pos];
						UncaptureUntil(runstack[--pos]);
						span = inputSpan.Slice(num);
						goto IL_0489;
						IL_0581:
						UncaptureUntil(capturePosition);
						if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
						{
							CheckTimeout();
						}
						if (num10 >= num11)
						{
							goto IL_0518;
						}
						num = --num11;
						span = inputSpan.Slice(num);
						goto IL_05b1;
						IL_0489:
						num5 = num;
						num4 = num;
						num3 = Crawlpos();
						if ((uint)span.Length >= 3u && span.StartsWith("tab", StringComparison.OrdinalIgnoreCase))
						{
							num += 3;
							span = inputSpan.Slice(num);
							num24 = 0;
							while (true)
							{
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
								num24++;
								if ((uint)span.Length < 3u || !span.StartsWith("let", StringComparison.OrdinalIgnoreCase))
								{
									break;
								}
								num += 3;
								span = inputSpan.Slice(num);
								if (num24 == 0)
								{
									continue;
								}
								goto IL_0552;
							}
							goto IL_0518;
						}
						goto IL_05c0;
						IL_0b6b:
						num2 = 8;
						goto IL_0d2d;
						IL_0690:
						num12 = num;
						if (!span.IsEmpty && (span[0] | 0x20) == 115)
						{
							span = span.Slice(1);
							num++;
						}
						num13 = num;
						goto IL_06ef;
						IL_0552:
						num10 = num;
						if (!span.IsEmpty && (span[0] | 0x20) == 115)
						{
							span = span.Slice(1);
							num++;
						}
						num11 = num;
						goto IL_05b1;
						IL_05c0:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length >= 3u && span.StartsWith("cap", StringComparison.OrdinalIgnoreCase))
						{
							num += 3;
							span = inputSpan.Slice(num);
							num25 = 0;
							while (true)
							{
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
								num25++;
								if ((uint)span.Length < 4u || !span.StartsWith("sule", StringComparison.OrdinalIgnoreCase))
								{
									break;
								}
								num += 4;
								span = inputSpan.Slice(num);
								if (num25 == 0)
								{
									continue;
								}
								goto IL_0690;
							}
							goto IL_0656;
						}
						goto IL_06fe;
						IL_0a62:
						UncaptureUntil(capturePosition3);
						if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
						{
							CheckTimeout();
						}
						if (num14 < num15)
						{
							num = --num15;
							span = inputSpan.Slice(num);
							goto IL_0a8f;
						}
						goto IL_0a9e;
						IL_0a9e:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length >= 4u && span.StartsWith("susp", StringComparison.OrdinalIgnoreCase))
						{
							num += 4;
							span = inputSpan.Slice(num);
							num28 = 0;
							while (true)
							{
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
								num28++;
								if ((uint)span.Length < 6u || !span.StartsWith("ension", StringComparison.OrdinalIgnoreCase))
								{
									break;
								}
								num += 6;
								span = inputSpan.Slice(num);
								if (num28 == 0)
								{
									continue;
								}
								goto IL_0b6b;
							}
							goto IL_0b34;
						}
						goto IL_0b72;
						IL_0343:
						_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, arg3, arg4, Crawlpos());
						arg5 = num;
						char c;
						if (!span.IsEmpty && (((c = span[0]) == '.') | (c == ':')))
						{
							span = span.Slice(1);
							num++;
						}
						arg6 = num;
						goto IL_03dd;
						IL_06fe:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length >= 2u && span.StartsWith("sy", StringComparison.OrdinalIgnoreCase) && (uint)span.Length >= 3u)
						{
							char c2 = span[2];
							if ((uint)c2 <= 82u)
							{
								if (c2 == 'P')
								{
									goto IL_076e;
								}
								if (c2 == 'R')
								{
									goto IL_077e;
								}
							}
							else
							{
								if (c2 == 'p')
								{
									goto IL_076e;
								}
								if (c2 == 'r')
								{
									goto IL_077e;
								}
							}
						}
						goto IL_07b7;
						IL_0a8f:
						capturePosition3 = Crawlpos();
						num2 = 7;
						goto IL_0d2d;
						IL_0921:
						if (--num27 >= 0)
						{
							num = runstack[--pos];
							UncaptureUntil(runstack[--pos]);
							span = inputSpan.Slice(num);
							goto IL_0958;
						}
						goto IL_095f;
						IL_0b72:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length < 6u || !span.StartsWith("lotion", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_0bbb;
						}
						num2 = 9;
						num += 6;
						span = inputSpan.Slice(num);
						goto IL_0d2d;
						IL_0518:
						if (--num24 >= 0)
						{
							num = runstack[--pos];
							UncaptureUntil(runstack[--pos]);
							span = inputSpan.Slice(num);
							goto IL_0552;
						}
						goto IL_05c0;
						IL_0bbb:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length < 6u || !span.StartsWith("sachet", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_0c04;
						}
						num2 = 10;
						num += 6;
						span = inputSpan.Slice(num);
						goto IL_0d2d;
						IL_0958:
						num2 = 4;
						goto IL_0d2d;
						IL_084d:
						if (--num26 >= 0)
						{
							num = runstack[--pos];
							UncaptureUntil(runstack[--pos]);
							span = inputSpan.Slice(num);
							goto IL_0884;
						}
						goto IL_088b;
						IL_077e:
						if ((uint)span.Length >= 5u && span.Slice(3).StartsWith("up", StringComparison.OrdinalIgnoreCase))
						{
							num += 5;
							span = inputSpan.Slice(num);
							goto IL_07b0;
						}
						goto IL_07b7;
						IL_05b1:
						capturePosition = Crawlpos();
						num2 = 0;
						goto IL_0d2d;
						IL_0c04:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length < 6u || !span.StartsWith("powder", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_0c4d;
						}
						num2 = 11;
						num += 6;
						span = inputSpan.Slice(num);
						goto IL_0d2d;
						IL_076e:
						num += 3;
						span = inputSpan.Slice(num);
						goto IL_07b0;
						IL_07b0:
						num2 = 2;
						goto IL_0d2d;
						IL_07b7:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length >= 3u && span.StartsWith("inj", StringComparison.OrdinalIgnoreCase))
						{
							num += 3;
							span = inputSpan.Slice(num);
							num26 = 0;
							while (true)
							{
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
								num26++;
								if ((uint)span.Length < 6u || !span.StartsWith("ection", StringComparison.OrdinalIgnoreCase))
								{
									break;
								}
								num += 6;
								span = inputSpan.Slice(num);
								if (num26 == 0)
								{
									continue;
								}
								goto IL_0884;
							}
							goto IL_084d;
						}
						goto IL_088b;
						IL_0884:
						num2 = 3;
						goto IL_0d2d;
						IL_06bf:
						UncaptureUntil(capturePosition2);
						if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
						{
							CheckTimeout();
						}
						if (num12 >= num13)
						{
							goto IL_0656;
						}
						num = --num13;
						span = inputSpan.Slice(num);
						goto IL_06ef;
						IL_0c4d:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length < 5u || !span.StartsWith("spray", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_0c96;
						}
						num2 = 12;
						num += 5;
						span = inputSpan.Slice(num);
						goto IL_0d2d;
						IL_088b:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length >= 4u && span.StartsWith("oint", StringComparison.OrdinalIgnoreCase))
						{
							num += 4;
							span = inputSpan.Slice(num);
							num27 = 0;
							while (true)
							{
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
								num27++;
								if ((uint)span.Length < 4u || !span.StartsWith("ment", StringComparison.OrdinalIgnoreCase))
								{
									break;
								}
								num += 4;
								span = inputSpan.Slice(num);
								if (num27 == 0)
								{
									continue;
								}
								goto IL_0958;
							}
							goto IL_0921;
						}
						goto IL_095f;
						IL_03dd:
						_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, arg5, arg6, Crawlpos());
						int n;
						for (n = 0; (uint)n < (uint)span.Length && char.IsWhiteSpace(span[n]); n++)
						{
						}
						span = span.Slice(n);
						num += n;
						if (num23 == 0)
						{
							continue;
						}
						goto IL_0489;
						IL_0470:
						if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
						{
							CheckTimeout();
						}
						if (num23 != 0)
						{
							UncaptureUntil(runstack[--pos]);
							_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPop(runstack, ref pos, out arg6, out arg5);
							if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
							{
								CheckTimeout();
							}
							if (arg5 >= arg6)
							{
								UncaptureUntil(runstack[--pos]);
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPop(runstack, ref pos, out arg4, out arg3);
								if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
								{
									CheckTimeout();
								}
								if (arg3 < arg4)
								{
									num = --arg4;
									span = inputSpan.Slice(num);
									goto IL_0343;
								}
								goto IL_0434;
							}
							num = --arg6;
							span = inputSpan.Slice(num);
							goto IL_03dd;
						}
						break;
						IL_0c96:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length < 7u || !span.StartsWith("inhaler", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_0470;
						}
						num2 = 13;
						num += 7;
						span = inputSpan.Slice(num);
						goto IL_0d2d;
						IL_095f:
						num = num4;
						span = inputSpan.Slice(num);
						UncaptureUntil(num3);
						if ((uint)span.Length < 5u || !span.StartsWith("cream", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_09a7;
						}
						num2 = 5;
						num += 5;
						span = inputSpan.Slice(num);
						goto IL_0d2d;
						IL_06ef:
						capturePosition2 = Crawlpos();
						num2 = 1;
						goto IL_0d2d;
						IL_0656:
						if (--num25 >= 0)
						{
							num = runstack[--pos];
							UncaptureUntil(runstack[--pos]);
							span = inputSpan.Slice(num);
							goto IL_0690;
						}
						goto IL_06fe;
					}
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num22 == 0)
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
						goto IL_01e9;
					}
					goto IL_0209;
					IL_01e9:
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, arg, arg2, Crawlpos());
					if (num22 == 0)
					{
						continue;
					}
					goto IL_0268;
				}
				UncaptureUntil(0);
				return false;
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				void UncaptureUntil(int num30)
				{
					while (Crawlpos() > num30)
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

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__FormPrefixRegex_8 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__FormPrefixRegex_8();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__FormPrefixRegex_8()
	{
		pattern = "^(?:\\d{1,2}\\s*[.)\\-]\\s*)?(?:rx\\s*[:.]?\\s*)?(?<form>tab(?:let)?s?|cap(?:sule)?s?|syp|syrup|inj(?:ection)?|oint(?:ment)?|cream|gel|drops?|susp(?:ension)?|lotion|sachet|powder|spray|inhaler)\\b\\.?\\s*(?<rest>.+)$";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		CapNames = new Hashtable
		{
			{ "0", 0 },
			{ "form", 1 },
			{ "rest", 2 }
		};
		capslist = new string[3] { "0", "form", "rest" };
		capsize = 3;
	}
}
