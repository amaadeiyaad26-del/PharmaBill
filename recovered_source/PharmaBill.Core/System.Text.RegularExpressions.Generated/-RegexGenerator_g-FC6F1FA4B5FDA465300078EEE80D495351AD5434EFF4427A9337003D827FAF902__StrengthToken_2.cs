using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__StrengthToken_2 : Regex
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
				int arg = 0;
				int arg2 = 0;
				int num4 = 0;
				int num5 = 0;
				int pos = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (num != 0)
				{
					return false;
				}
				num2 = num;
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
				num3 = num;
				num2++;
				while (true)
				{
					num4 = 0;
					while (true)
					{
						_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, num);
						num4++;
						if (!span.IsEmpty && span[0] == '.')
						{
							num++;
							span = inputSpan.Slice(num);
							arg = num;
							int j;
							for (j = 0; (uint)j < (uint)span.Length && char.IsDigit(span[j]); j++)
							{
							}
							if (j != 0)
							{
								span = span.Slice(j);
								num += j;
								arg2 = num;
								arg++;
								goto IL_0155;
							}
						}
						goto IL_016f;
						IL_0155:
						_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, arg, arg2);
						if (num4 == 0)
						{
							continue;
						}
						goto IL_01b0;
						IL_01b0:
						num5 = 0;
						while (true)
						{
							_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, num);
							num5++;
							int num6 = num;
							if (!span.IsEmpty && span[0] == 'm' && (uint)span.Length >= 2u)
							{
								char c = span[1];
								if (c != 'c')
								{
									if (c == 'g')
									{
										num += 2;
										span = inputSpan.Slice(num);
										goto IL_0236;
									}
								}
								else if ((uint)span.Length >= 3u && span[2] == 'g')
								{
									num += 3;
									span = inputSpan.Slice(num);
									goto IL_0236;
								}
							}
							goto IL_024b;
							IL_02d4:
							num = num6;
							span = inputSpan.Slice(num);
							if (span.StartsWith("iu"))
							{
								_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, 3, num6);
								num += 2;
								span = inputSpan.Slice(num);
								goto IL_0358;
							}
							goto IL_0361;
							IL_0236:
							_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, 0, num6);
							goto IL_0358;
							IL_024b:
							num = num6;
							span = inputSpan.Slice(num);
							if (span.IsEmpty || span[0] != 'g')
							{
								goto IL_0291;
							}
							_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, 1, num6);
							num++;
							span = inputSpan.Slice(num);
							goto IL_0358;
							IL_03a2:
							if (num < inputSpan.Length - 1 || ((uint)num < (uint)inputSpan.Length && inputSpan[num] != '\n'))
							{
								if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
								{
									CheckTimeout();
								}
								if (num5 != 0)
								{
									if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
									{
										CheckTimeout();
									}
									num6 = runstack[--pos];
									switch (runstack[--pos])
									{
									case 0:
										break;
									case 1:
										goto IL_0291;
									case 2:
										goto IL_02d4;
									default:
										goto IL_0358;
									case 3:
										goto IL_0361;
									}
									goto IL_024b;
								}
								break;
							}
							runtextpos = num;
							Capture(0, start, num);
							return true;
							IL_0291:
							num = num6;
							span = inputSpan.Slice(num);
							if (!span.StartsWith("ml"))
							{
								goto IL_02d4;
							}
							_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, 2, num6);
							num += 2;
							span = inputSpan.Slice(num);
							goto IL_0358;
							IL_0361:
							if (--num5 < 0)
							{
								break;
							}
							num = runstack[--pos];
							span = inputSpan.Slice(num);
							goto IL_03a2;
							IL_0358:
							if (num5 == 0)
							{
								continue;
							}
							goto IL_03a2;
						}
						if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
						{
							CheckTimeout();
						}
						if (num4 != 0)
						{
							_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPop(runstack, ref pos, out arg2, out arg);
							if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
							{
								CheckTimeout();
							}
							if (arg < arg2)
							{
								num = --arg2;
								span = inputSpan.Slice(num);
								goto IL_0155;
							}
							goto IL_016f;
						}
						break;
						IL_016f:
						if (--num4 < 0)
						{
							break;
						}
						num = runstack[--pos];
						span = inputSpan.Slice(num);
						goto IL_01b0;
					}
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num2 >= num3)
					{
						break;
					}
					num = --num3;
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

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__StrengthToken_2 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__StrengthToken_2();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__StrengthToken_2()
	{
		pattern = "^\\d+(?:\\.\\d+)?(?:mg|mcg|g|ml|iu)?$";
		roptions = RegexOptions.None;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
