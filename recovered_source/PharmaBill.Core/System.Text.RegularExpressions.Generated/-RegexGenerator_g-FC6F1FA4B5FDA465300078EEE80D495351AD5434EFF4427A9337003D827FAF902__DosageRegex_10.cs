using System.CodeDom.Compiler;
using System.Collections;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DosageRegex_10 : Regex
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
					int num2 = inputSpan.Slice(num).IndexOfNonAsciiOrAny_22F379E94C9DFDFD07A5ECBCA0459F0F6BFC8B0386C0D244B4AE253FBF2DC6B6();
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
				int pos = 0;
				int num17 = 0;
				int num18 = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				int num19 = pos;
				int num20 = num;
				int capturePosition3 = Crawlpos();
				span = inputSpan.Slice(num);
				int num21 = num;
				if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
				{
					CheckTimeout();
				}
				char c;
				if ((uint)(num - 1) < inputSpan.Length && !(((c = inputSpan[num - 1]) < '\u0080') ? (("\0\0䀀Ͽ\0\0\0\0"[(int)c >> 4] & (1 << (c & 0xF))) == 0) : (!RegexRunner.CharInClass(c, "\0\u0002\u0001./\t"))))
				{
					num--;
				}
				else
				{
					num = num21;
					span = inputSpan.Slice(num);
					num5 = num;
					if (!span.IsEmpty && char.IsDigit(span[0]))
					{
						num++;
						span = inputSpan.Slice(num);
						num17 = pos;
						num11 = 0;
						while (true)
						{
							_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
							num11++;
							if ((uint)span.Length < 2u || span[0] != '.' || !char.IsDigit(span[1]))
							{
								break;
							}
							num += 2;
							span = inputSpan.Slice(num);
							if (num11 == 0)
							{
								continue;
							}
							goto IL_01a1;
						}
						if (--num11 >= 0)
						{
							num = runstack[--pos];
							UncaptureUntil(runstack[--pos]);
							span = inputSpan.Slice(num);
							goto IL_01a1;
						}
					}
				}
				goto IL_06e6;
				IL_0f28:
				if ((uint)span.Length >= 2u && span.StartsWith("da", StringComparison.OrdinalIgnoreCase) && (uint)span.Length >= 3u)
				{
					char c2 = span[2];
					if ((uint)c2 <= 89u)
					{
						if (c2 != 'I')
						{
							if (c2 != 'Y')
							{
								goto IL_0eee;
							}
							goto IL_0f81;
						}
					}
					else if (c2 != 'i')
					{
						if (c2 != 'y')
						{
							goto IL_0eee;
						}
						goto IL_0f81;
					}
					if ((uint)span.Length >= 5u && span.Slice(3).StartsWith("ly", StringComparison.OrdinalIgnoreCase))
					{
						num += 5;
						span = inputSpan.Slice(num);
						goto IL_0fc9;
					}
				}
				goto IL_0eee;
				IL_088d:
				if ((uint)span.Length >= 3u && (span[2] | 0x20) == 100)
				{
					num += 3;
					span = inputSpan.Slice(num);
					goto IL_08b5;
				}
				goto IL_08bc;
				IL_0f81:
				num += 3;
				span = inputSpan.Slice(num);
				goto IL_0fc9;
				IL_0a17:
				if ((uint)span.Length >= 3u && (span[2] | 0x20) == 115)
				{
					num += 3;
					span = inputSpan.Slice(num);
					goto IL_0a73;
				}
				goto IL_0a7a;
				IL_06d4:
				int num22;
				num = num22;
				span = inputSpan.Slice(num);
				goto IL_1017;
				IL_0961:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if ((uint)span.Length < 2u || !span.StartsWith("hs", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_09a9;
				}
				num2 = 4;
				num += 2;
				span = inputSpan.Slice(num);
				goto IL_1004;
				IL_07b6:
				num += 2;
				span = inputSpan.Slice(num);
				goto IL_07ee;
				IL_08b5:
				num2 = 2;
				goto IL_1004;
				IL_08bc:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if (!span.IsEmpty && (span[0] | 0x20) == 113 && (uint)span.Length >= 2u)
				{
					char c2 = span[1];
					if ((uint)c2 <= 73u)
					{
						if (c2 == 'D')
						{
							goto IL_094c;
						}
						if (c2 == 'I')
						{
							goto IL_0922;
						}
					}
					else
					{
						if (c2 == 'd')
						{
							goto IL_094c;
						}
						if (c2 == 'i')
						{
							goto IL_0922;
						}
					}
				}
				goto IL_0961;
				IL_042b:
				if (--num13 >= 0)
				{
					num = runstack[--pos];
					UncaptureUntil(runstack[--pos]);
					span = inputSpan.Slice(num);
					goto IL_0465;
				}
				goto IL_06e6;
				IL_0eee:
				if (--num16 < 0)
				{
					UncaptureUntil(capturePosition2);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num9 >= num10)
					{
						UncaptureUntil(capturePosition);
						if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
						{
							CheckTimeout();
						}
						if (num7 >= num8)
						{
							UncaptureUntil(0);
							return false;
						}
						num = --num8;
						span = inputSpan.Slice(num);
						goto IL_0dfc;
					}
					num = --num10;
					span = inputSpan.Slice(num);
					goto IL_0e73;
				}
				num = runstack[--pos];
				UncaptureUntil(runstack[--pos]);
				span = inputSpan.Slice(num);
				goto IL_0f28;
				IL_07c6:
				if ((uint)span.Length >= 3u && (span[2] | 0x20) == 100)
				{
					num += 3;
					span = inputSpan.Slice(num);
					goto IL_07ee;
				}
				goto IL_07f5;
				IL_0cf4:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if (span.IsEmpty || !char.IsDigit(span[0]))
				{
					UncaptureUntil(0);
					return false;
				}
				int i;
				for (i = 1; (uint)i < (uint)span.Length && char.IsWhiteSpace(span[i]); i++)
				{
				}
				span = span.Slice(i);
				num += i;
				if ((uint)span.Length < 4u || !span.StartsWith("time", StringComparison.OrdinalIgnoreCase))
				{
					UncaptureUntil(0);
					return false;
				}
				num += 4;
				span = inputSpan.Slice(num);
				num7 = num;
				if (!span.IsEmpty && (span[0] | 0x20) == 115)
				{
					span = span.Slice(1);
					num++;
				}
				num8 = num;
				goto IL_0dfc;
				IL_0ced:
				num2 = 7;
				goto IL_1004;
				IL_09a9:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if (!span.IsEmpty && (span[0] | 0x20) == 115 && (uint)span.Length >= 2u)
				{
					char c2 = span[1];
					if ((uint)c2 <= 84u)
					{
						if (c2 == 'O')
						{
							goto IL_0a17;
						}
						if (c2 == 'T')
						{
							goto IL_0a41;
						}
					}
					else
					{
						if (c2 == 'o')
						{
							goto IL_0a17;
						}
						if (c2 == 't')
						{
							goto IL_0a41;
						}
					}
				}
				goto IL_0a7a;
				IL_01a1:
				pos = num17;
				int j;
				for (j = 0; (uint)j < (uint)span.Length && char.IsWhiteSpace(span[j]); j++)
				{
				}
				span = span.Slice(j);
				num += j;
				if (!span.IsEmpty && !(((c = span[0]) != '-') & (c != '–')))
				{
					int k;
					for (k = 1; (uint)k < (uint)span.Length && char.IsWhiteSpace(span[k]); k++)
					{
					}
					span = span.Slice(k);
					num += k;
					if (!span.IsEmpty && char.IsDigit(span[0]))
					{
						num++;
						span = inputSpan.Slice(num);
						num18 = pos;
						num12 = 0;
						while (true)
						{
							_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
							num12++;
							if ((uint)span.Length < 2u || span[0] != '.' || !char.IsDigit(span[1]))
							{
								break;
							}
							num += 2;
							span = inputSpan.Slice(num);
							if (num12 == 0)
							{
								continue;
							}
							goto IL_0305;
						}
						if (--num12 >= 0)
						{
							num = runstack[--pos];
							UncaptureUntil(runstack[--pos]);
							span = inputSpan.Slice(num);
							goto IL_0305;
						}
					}
				}
				goto IL_06e6;
				IL_095a:
				num2 = 3;
				goto IL_1004;
				IL_0a41:
				if ((uint)span.Length >= 4u && span.Slice(2).StartsWith("at", StringComparison.OrdinalIgnoreCase))
				{
					num += 4;
					span = inputSpan.Slice(num);
					goto IL_0a73;
				}
				goto IL_0a7a;
				IL_0fc9:
				num2 = 8;
				goto IL_1004;
				IL_0465:
				num14 = 0;
				while (true)
				{
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
					num14++;
					int l;
					for (l = 0; (uint)l < (uint)span.Length && char.IsWhiteSpace(span[l]); l++)
					{
					}
					span = span.Slice(l);
					num += l;
					if (!span.IsEmpty && !(((c = span[0]) != '-') & (c != '–')))
					{
						int m;
						for (m = 1; (uint)m < (uint)span.Length && char.IsWhiteSpace(span[m]); m++)
						{
						}
						span = span.Slice(m);
						num += m;
						if (!span.IsEmpty && char.IsDigit(span[0]))
						{
							num++;
							span = inputSpan.Slice(num);
							num15 = 0;
							while (true)
							{
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
								num15++;
								if ((uint)span.Length < 2u || span[0] != '.' || !char.IsDigit(span[1]))
								{
									break;
								}
								num += 2;
								span = inputSpan.Slice(num);
								if (num15 == 0)
								{
									continue;
								}
								goto IL_05db;
							}
							goto IL_05a4;
						}
					}
					goto IL_0614;
					IL_05a4:
					if (--num15 >= 0)
					{
						num = runstack[--pos];
						UncaptureUntil(runstack[--pos]);
						span = inputSpan.Slice(num);
						goto IL_05db;
					}
					goto IL_0614;
					IL_0614:
					if (--num14 < 0)
					{
						break;
					}
					num = runstack[--pos];
					UncaptureUntil(runstack[--pos]);
					span = inputSpan.Slice(num);
					goto IL_0666;
					IL_05db:
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, num15);
					if (num14 == 0)
					{
						continue;
					}
					goto IL_0666;
					IL_0666:
					Capture(1, num5, num);
					span = inputSpan.Slice(num);
					num22 = num;
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (!span.IsEmpty && !(((c = span[0]) < '\u0080') ? (("\0\0䀀Ͽ\0\0\0\0"[(int)c >> 4] & (1 << (c & 0xF))) == 0) : (!RegexRunner.CharInClass(c, "\0\u0002\u0001./\t"))))
					{
						if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
						{
							CheckTimeout();
						}
						if (num14 != 0)
						{
							num15 = runstack[--pos];
							if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
							{
								CheckTimeout();
							}
							goto IL_05a4;
						}
						break;
					}
					goto IL_06d4;
				}
				goto IL_042b;
				IL_074e:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if (!span.IsEmpty && (span[0] | 0x20) == 98 && (uint)span.Length >= 2u)
				{
					char c2 = span[1];
					if ((uint)c2 <= 73u)
					{
						if (c2 == 'D')
						{
							goto IL_07b6;
						}
						if (c2 == 'I')
						{
							goto IL_07c6;
						}
					}
					else
					{
						if (c2 == 'd')
						{
							goto IL_07b6;
						}
						if (c2 == 'i')
						{
							goto IL_07c6;
						}
					}
				}
				goto IL_07f5;
				IL_0863:
				if ((uint)span.Length >= 3u && (span[2] | 0x20) == 115)
				{
					num += 3;
					span = inputSpan.Slice(num);
					goto IL_08b5;
				}
				goto IL_08bc;
				IL_0c51:
				if ((uint)span.Length >= 6u && span.Slice(2).StartsWith("rice", StringComparison.OrdinalIgnoreCase))
				{
					num += 6;
					span = inputSpan.Slice(num);
					int n;
					for (n = 0; (uint)n < (uint)span.Length && char.IsWhiteSpace(span[n]); n++)
					{
					}
					if (n != 0)
					{
						span = span.Slice(n);
						num += n;
						if ((uint)span.Length >= 5u && span.StartsWith("daily", StringComparison.OrdinalIgnoreCase))
						{
							num += 5;
							span = inputSpan.Slice(num);
							goto IL_0ced;
						}
					}
				}
				goto IL_0cf4;
				IL_06e6:
				num = num20;
				span = inputSpan.Slice(num);
				UncaptureUntil(capturePosition3);
				if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsBoundary(inputSpan, num))
				{
					UncaptureUntil(0);
					return false;
				}
				num6 = num;
				num4 = num;
				num3 = Crawlpos();
				if ((uint)span.Length < 2u || !span.StartsWith("od", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_074e;
				}
				num2 = 0;
				num += 2;
				span = inputSpan.Slice(num);
				goto IL_1004;
				IL_0ba4:
				if ((uint)span.Length >= 5u && span.Slice(2).StartsWith("ice", StringComparison.OrdinalIgnoreCase))
				{
					num += 5;
					span = inputSpan.Slice(num);
					int num23;
					for (num23 = 0; (uint)num23 < (uint)span.Length && char.IsWhiteSpace(span[num23]); num23++)
					{
					}
					if (num23 != 0)
					{
						span = span.Slice(num23);
						num += num23;
						if ((uint)span.Length >= 5u && span.StartsWith("daily", StringComparison.OrdinalIgnoreCase))
						{
							num += 5;
							span = inputSpan.Slice(num);
							goto IL_0ced;
						}
					}
				}
				goto IL_0cf4;
				IL_07f5:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if (!span.IsEmpty && (span[0] | 0x20) == 116 && (uint)span.Length >= 2u)
				{
					char c2 = span[1];
					if ((uint)c2 <= 73u)
					{
						if (c2 == 'D')
						{
							goto IL_0863;
						}
						if (c2 == 'I')
						{
							goto IL_088d;
						}
					}
					else
					{
						if (c2 == 'd')
						{
							goto IL_0863;
						}
						if (c2 == 'i')
						{
							goto IL_088d;
						}
					}
				}
				goto IL_08bc;
				IL_0a73:
				num2 = 5;
				goto IL_1004;
				IL_0b2c:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if (!span.IsEmpty && (span[0] | 0x20) == 116 && (uint)span.Length >= 2u)
				{
					char c2 = span[1];
					if ((uint)c2 <= 87u)
					{
						if (c2 == 'H')
						{
							goto IL_0c51;
						}
						if (c2 == 'W')
						{
							goto IL_0ba4;
						}
					}
					else
					{
						if (c2 == 'h')
						{
							goto IL_0c51;
						}
						if (c2 == 'w')
						{
							goto IL_0ba4;
						}
					}
				}
				goto IL_0cf4;
				IL_0a7a:
				num = num4;
				span = inputSpan.Slice(num);
				UncaptureUntil(num3);
				if ((uint)span.Length >= 4u && span.StartsWith("once", StringComparison.OrdinalIgnoreCase))
				{
					num += 4;
					span = inputSpan.Slice(num);
					int num24;
					for (num24 = 0; (uint)num24 < (uint)span.Length && char.IsWhiteSpace(span[num24]); num24++)
					{
					}
					if (num24 != 0)
					{
						span = span.Slice(num24);
						num += num24;
						if ((uint)span.Length >= 5u && span.StartsWith("daily", StringComparison.OrdinalIgnoreCase))
						{
							num2 = 6;
							num += 5;
							span = inputSpan.Slice(num);
							goto IL_1004;
						}
					}
				}
				goto IL_0b2c;
				IL_1017:
				pos = num19;
				runtextpos = num;
				Capture(0, start, num);
				return true;
				IL_07ee:
				num2 = 1;
				goto IL_1004;
				IL_0dfc:
				capturePosition = Crawlpos();
				num9 = num;
				int num25;
				for (num25 = 0; (uint)num25 < (uint)span.Length && char.IsWhiteSpace(span[num25]); num25++)
				{
				}
				span = span.Slice(num25);
				num += num25;
				num10 = num;
				goto IL_0e73;
				IL_1004:
				while (true)
				{
					Capture(2, num6, num);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPostWordCharBoundary(inputSpan, num))
					{
						break;
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
						goto IL_07f5;
					case 2:
						goto IL_08bc;
					case 3:
						goto IL_0961;
					case 4:
						goto IL_09a9;
					case 5:
						goto IL_0a7a;
					case 6:
						goto IL_0b2c;
					case 7:
						goto IL_0cf4;
					case 8:
						goto IL_0eee;
					default:
						continue;
					}
					goto IL_074e;
				}
				goto IL_1017;
				IL_094c:
				num += 2;
				span = inputSpan.Slice(num);
				goto IL_095a;
				IL_0305:
				pos = num18;
				int num26;
				for (num26 = 0; (uint)num26 < (uint)span.Length && char.IsWhiteSpace(span[num26]); num26++)
				{
				}
				span = span.Slice(num26);
				num += num26;
				if (!span.IsEmpty && !(((c = span[0]) != '-') & (c != '–')))
				{
					int num27;
					for (num27 = 1; (uint)num27 < (uint)span.Length && char.IsWhiteSpace(span[num27]); num27++)
					{
					}
					span = span.Slice(num27);
					num += num27;
					if (!span.IsEmpty && char.IsDigit(span[0]))
					{
						num++;
						span = inputSpan.Slice(num);
						num13 = 0;
						while (true)
						{
							_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
							num13++;
							if ((uint)span.Length < 2u || span[0] != '.' || !char.IsDigit(span[1]))
							{
								break;
							}
							num += 2;
							span = inputSpan.Slice(num);
							if (num13 == 0)
							{
								continue;
							}
							goto IL_0465;
						}
						goto IL_042b;
					}
				}
				goto IL_06e6;
				IL_0e73:
				capturePosition2 = Crawlpos();
				num16 = 0;
				while (true)
				{
					_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
					num16++;
					if (span.IsEmpty || (span[0] | 0x20) != 97)
					{
						break;
					}
					int num28;
					for (num28 = 1; (uint)num28 < (uint)span.Length && char.IsWhiteSpace(span[num28]); num28++)
					{
					}
					span = span.Slice(num28);
					num += num28;
					if (num16 == 0)
					{
						continue;
					}
					goto IL_0f28;
				}
				goto IL_0eee;
				IL_0922:
				if ((uint)span.Length >= 3u && (span[2] | 0x20) == 100)
				{
					num += 3;
					span = inputSpan.Slice(num);
					goto IL_095a;
				}
				goto IL_0961;
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				void UncaptureUntil(int num29)
				{
					while (Crawlpos() > num29)
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

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DosageRegex_10 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DosageRegex_10();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DosageRegex_10()
	{
		pattern = "(?<![\\d.])(?<tri>\\d(?:\\.\\d)?\\s*[-–]\\s*\\d(?:\\.\\d)?\\s*[-–]\\s*\\d(?:\\.\\d)?(?:\\s*[-–]\\s*\\d(?:\\.\\d)?)?)(?![\\d.])|\\b(?<abbr>OD|BD|BID|TDS|TID|QID|QD|HS|SOS|STAT|once\\s+daily|twice\\s+daily|thrice\\s+daily|\\d\\s*times?\\s*(?:a\\s*)?(?:day|daily))\\b";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		CapNames = new Hashtable
		{
			{ "0", 0 },
			{ "abbr", 2 },
			{ "tri", 1 }
		};
		capslist = new string[3] { "0", "tri", "abbr" };
		capsize = 3;
	}
}
