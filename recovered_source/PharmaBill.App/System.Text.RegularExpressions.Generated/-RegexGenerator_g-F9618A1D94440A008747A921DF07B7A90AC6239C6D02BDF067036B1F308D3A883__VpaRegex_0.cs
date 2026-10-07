using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EF9618A1D94440A008747A921DF07B7A90AC6239C6D02BDF067036B1F308D3A883__VpaRegex_0 : Regex
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
				if (num <= inputSpan.Length - 5 && num == 0)
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
				ReadOnlySpan<char> readOnlySpan = inputSpan.Slice(num);
				if (num != 0)
				{
					return false;
				}
				int i;
				for (i = 0; i < 256 && (uint)i < (uint)readOnlySpan.Length; i++)
				{
					char c;
					if ((c = readOnlySpan[i]) >= '{')
					{
						break;
					}
					if (("\0\0怀Ͽ\ufffe蟿\ufffe߿"[(int)c >> 4] & (1 << (c & 0xF))) == 0)
					{
						break;
					}
				}
				if (i < 2)
				{
					return false;
				}
				readOnlySpan = readOnlySpan.Slice(i);
				num += i;
				if ((uint)readOnlySpan.Length < 2u || readOnlySpan[0] != '@' || !char.IsAsciiLetter(readOnlySpan[1]))
				{
					return false;
				}
				num += 2;
				readOnlySpan = inputSpan.Slice(num);
				int j;
				for (j = 0; j < 64 && (uint)j < (uint)readOnlySpan.Length; j++)
				{
					char c;
					if ((c = readOnlySpan[j]) >= '{')
					{
						break;
					}
					if (("\0\0怀Ͽ\ufffe߿\ufffe߿"[(int)c >> 4] & (1 << (c & 0xF))) == 0)
					{
						break;
					}
				}
				if (j == 0)
				{
					return false;
				}
				readOnlySpan = readOnlySpan.Slice(j);
				num += j;
				if (num < inputSpan.Length - 1 || ((uint)num < (uint)inputSpan.Length && inputSpan[num] != '\n'))
				{
					return false;
				}
				runtextpos = num;
				Capture(0, start, num);
				return true;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EF9618A1D94440A008747A921DF07B7A90AC6239C6D02BDF067036B1F308D3A883__VpaRegex_0 Instance = new _003CRegexGenerator_g_003EF9618A1D94440A008747A921DF07B7A90AC6239C6D02BDF067036B1F308D3A883__VpaRegex_0();

	private _003CRegexGenerator_g_003EF9618A1D94440A008747A921DF07B7A90AC6239C6D02BDF067036B1F308D3A883__VpaRegex_0()
	{
		pattern = "^[A-Za-z0-9._-]{2,256}@[A-Za-z][A-Za-z0-9.-]{1,64}$";
		roptions = RegexOptions.CultureInvariant;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EF9618A1D94440A008747A921DF07B7A90AC6239C6D02BDF067036B1F308D3A883__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EF9618A1D94440A008747A921DF07B7A90AC6239C6D02BDF067036B1F308D3A883__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
