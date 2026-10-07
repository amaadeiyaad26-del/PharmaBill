using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__HeaderLineRegex_12 : Regex
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
					int num2 = inputSpan.Slice(num).IndexOfAny(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_indexOfAnyStrings_OrdinalIgnoreCase_95F97D95F39C0977E19DF37421C4FA89F00399940FCACC8AC12D7B6429EA66D6);
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
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPreWordCharBoundary(inputSpan, num))
				{
					return false;
				}
				num5 = num;
				if ((uint)span.Length < 2u || !span.StartsWith("dr", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_0065;
				}
				num2 = 0;
				num += 2;
				span = inputSpan.Slice(num);
				goto IL_0480;
				IL_023a:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length >= 2u && span.StartsWith("mo", StringComparison.OrdinalIgnoreCase))
				{
					num7 = num;
					if ((uint)span.Length < 3u || (span[2] | 0x20) != 98)
					{
						goto IL_029b;
					}
					num4 = 0;
					num += 3;
					span = inputSpan.Slice(num);
					goto IL_02f5;
				}
				goto IL_02fc;
				IL_01ba:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 4u || !span.StartsWith("date", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_01fa;
				}
				num2 = 3;
				num += 4;
				span = inputSpan.Slice(num);
				goto IL_0480;
				IL_00c9:
				num = num6;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 4u || !span.Slice(2).StartsWith("gd", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_010d;
				}
				num3 = 1;
				num += 4;
				span = inputSpan.Slice(num);
				goto IL_0173;
				IL_0480:
				while (true)
				{
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPostWordCharBoundary(inputSpan, num))
					{
						int i;
						for (i = 0; (uint)i < (uint)span.Length && char.IsWhiteSpace(span[i]); i++)
						{
						}
						span = span.Slice(i);
						num += i;
						char c;
						if (!span.IsEmpty && !(((c = span[0]) != '.') & (c != ':')))
						{
							Capture(0, start, runtextpos = num + 1);
							return true;
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
						goto IL_0153;
					case 2:
						goto IL_01ba;
					case 3:
						goto IL_01fa;
					case 4:
						goto IL_023a;
					case 5:
						goto IL_02df;
					case 6:
						goto IL_033c;
					case 7:
						goto IL_037c;
					case 8:
						goto IL_03bc;
					case 9:
						goto IL_03fd;
					case 10:
						return false;
					default:
						continue;
					}
					break;
				}
				goto IL_0065;
				IL_033c:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 6u || !span.StartsWith("clinic", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_037c;
				}
				num2 = 7;
				num += 6;
				span = inputSpan.Slice(num);
				goto IL_0480;
				IL_02f5:
				num2 = 5;
				goto IL_0480;
				IL_02fc:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 7u || !span.StartsWith("address", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_033c;
				}
				num2 = 6;
				num += 7;
				span = inputSpan.Slice(num);
				goto IL_0480;
				IL_0153:
				if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
				{
					CheckTimeout();
				}
				switch (num3)
				{
				case 0:
					break;
				case 1:
					goto IL_010d;
				default:
					goto IL_0173;
				case 2:
					goto IL_017a;
				}
				goto IL_00c9;
				IL_029b:
				num = num7;
				span = inputSpan.Slice(num);
				if ((uint)span.Length >= 6u && span.Slice(2).StartsWith("bile", StringComparison.OrdinalIgnoreCase))
				{
					num4 = 1;
					num += 6;
					span = inputSpan.Slice(num);
					goto IL_02f5;
				}
				goto IL_02fc;
				IL_03fd:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 4u || !span.StartsWith("name", StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
				num2 = 10;
				num += 4;
				span = inputSpan.Slice(num);
				goto IL_0480;
				IL_02df:
				if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
				{
					CheckTimeout();
				}
				if (num4 == 0)
				{
					goto IL_029b;
				}
				if (num4 != 1)
				{
					goto IL_02f5;
				}
				goto IL_02fc;
				IL_03bc:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 3u || !span.StartsWith("age", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_03fd;
				}
				num2 = 9;
				num += 3;
				span = inputSpan.Slice(num);
				goto IL_0480;
				IL_0173:
				num2 = 1;
				goto IL_0480;
				IL_0065:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length >= 2u && span.StartsWith("re", StringComparison.OrdinalIgnoreCase))
				{
					num6 = num;
					if ((uint)span.Length < 3u || (span[2] | 0x20) != 103)
					{
						goto IL_00c9;
					}
					num3 = 0;
					num += 3;
					span = inputSpan.Slice(num);
					goto IL_0173;
				}
				goto IL_017a;
				IL_01fa:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 5u || !span.StartsWith("phone", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_023a;
				}
				num2 = 4;
				num += 5;
				span = inputSpan.Slice(num);
				goto IL_0480;
				IL_017a:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 7u || !span.StartsWith("patient", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_01ba;
				}
				num2 = 2;
				num += 7;
				span = inputSpan.Slice(num);
				goto IL_0480;
				IL_010d:
				num = num6;
				span = inputSpan.Slice(num);
				if ((uint)span.Length >= 12u && span.Slice(2).StartsWith("gistration", StringComparison.OrdinalIgnoreCase))
				{
					num3 = 2;
					num += 12;
					span = inputSpan.Slice(num);
					goto IL_0173;
				}
				goto IL_017a;
				IL_037c:
				num = num5;
				span = inputSpan.Slice(num);
				if ((uint)span.Length < 8u || !span.StartsWith("hospital", StringComparison.OrdinalIgnoreCase))
				{
					goto IL_03bc;
				}
				num2 = 8;
				num += 8;
				span = inputSpan.Slice(num);
				goto IL_0480;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__HeaderLineRegex_12 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__HeaderLineRegex_12();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__HeaderLineRegex_12()
	{
		pattern = "\\b(?:dr|reg|regd|registration|patient|date|phone|mob|mobile|address|clinic|hospital|age|name)\\b\\s*[.:]";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
