using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__StrengthRegex_9 : Regex
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
					int num2 = inputSpan.Slice(num).IndexOfAnyDigit();
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
				int pos = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				span = inputSpan.Slice(num);
				int num7 = num;
				if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
				{
					CheckTimeout();
				}
				char c;
				if ((uint)(num - 1) < inputSpan.Length && !(((c = inputSpan[num - 1]) < '\u0080') ? (("\0\0䀀Ͽ\0\0\0\0"[(int)c >> 4] & (1 << (c & 0xF))) == 0) : (!RegexRunner.CharInClass(c, "\0\u0002\u0001./\t"))))
				{
					num--;
					return false;
				}
				num = num7;
				span = inputSpan.Slice(num);
				num4 = num;
				int i;
				for (i = 0; (uint)i < (uint)span.Length && char.IsDigit(span[i]); i++)
				{
				}
				if (i == 0)
				{
					return false;
				}
				span = span.Slice(i);
				num += i;
				num5 = num;
				num4++;
				while (true)
				{
					num6 = 0;
					while (true)
					{
						_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, num);
						num6++;
						if (!span.IsEmpty && span[0] == '.')
						{
							num++;
							span = inputSpan.Slice(num);
							int j;
							for (j = 0; (uint)j < (uint)span.Length && char.IsDigit(span[j]); j++)
							{
							}
							if (j != 0)
							{
								span = span.Slice(j);
								num += j;
								if (num6 == 0)
								{
									continue;
								}
								goto IL_01b3;
							}
						}
						goto IL_018d;
						IL_01b3:
						int k;
						for (k = 0; (uint)k < (uint)span.Length && char.IsWhiteSpace(span[k]); k++)
						{
						}
						span = span.Slice(k);
						num += k;
						num3 = num;
						if ((uint)span.Length < 2u || !span.StartsWith("mg", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_0220;
						}
						num2 = 0;
						num += 2;
						span = inputSpan.Slice(num);
						goto IL_0342;
						IL_0342:
						while (true)
						{
							if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPostWordCharBoundary(inputSpan, num))
							{
								if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
								{
									CheckTimeout();
								}
								switch (num2)
								{
								case 4:
									break;
								case 0:
									goto IL_0220;
								case 1:
									goto IL_0260;
								case 2:
									goto IL_029b;
								case 3:
									goto IL_02d8;
								default:
									continue;
								}
								break;
							}
							runtextpos = num;
							Capture(0, start, num);
							return true;
						}
						goto IL_018d;
						IL_029b:
						num = num3;
						span = inputSpan.Slice(num);
						if ((uint)span.Length < 2u || !span.StartsWith("ml", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_02d8;
						}
						num2 = 3;
						num += 2;
						span = inputSpan.Slice(num);
						goto IL_0342;
						IL_0260:
						num = num3;
						span = inputSpan.Slice(num);
						if (span.IsEmpty || (span[0] | 0x20) != 103)
						{
							goto IL_029b;
						}
						num2 = 2;
						num++;
						span = inputSpan.Slice(num);
						goto IL_0342;
						IL_02d8:
						num = num3;
						span = inputSpan.Slice(num);
						if ((uint)span.Length < 2u || !span.StartsWith("iu", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_018d;
						}
						num2 = 4;
						num += 2;
						span = inputSpan.Slice(num);
						goto IL_0342;
						IL_018d:
						if (--num6 < 0)
						{
							break;
						}
						num = runstack[--pos];
						span = inputSpan.Slice(num);
						goto IL_01b3;
						IL_0220:
						num = num3;
						span = inputSpan.Slice(num);
						if ((uint)span.Length < 3u || !span.StartsWith("mcg", StringComparison.OrdinalIgnoreCase))
						{
							goto IL_0260;
						}
						num2 = 1;
						num += 3;
						span = inputSpan.Slice(num);
						goto IL_0342;
					}
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num4 >= num5)
					{
						break;
					}
					num = --num5;
					span = inputSpan.Slice(num);
				}
				return false;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__StrengthRegex_9 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__StrengthRegex_9();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__StrengthRegex_9()
	{
		pattern = "(?<![\\d.])\\d+(?:\\.\\d+)?\\s*(?:mg|mcg|g|ml|iu)\\b";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
