namespace EncDotNet.S100.TestSupport;

/// <summary>
/// <c>table.sort</c> with the algorithm of reference Lua 5.1/5.2
/// (<c>ltablib.c</c> <c>auxsort</c>: quicksort with a median-of-three pivot),
/// written in Lua for these tests.
/// </summary>
/// <remarks>
/// Lua's sort isn't stable, so the order of elements that compare equal is
/// whatever the algorithm leaves. Lua-CSharp ports the reference algorithm;
/// MoonSharp's sort is stable, so the S-101 rules that sort sectors by range
/// (<c>LightSectored.lua</c>) list a light's colours in a different order on
/// it. The differential test installs this on the MoonSharp side so it
/// compares everything else, and the scripting tests check Lua-CSharp's sort
/// against it. Checked against reference Lua 5.2 on 2,000 random arrays
/// with ties.
/// </remarks>
internal static class ReferenceLuaSort
{
    /// <summary>A chunk that replaces <c>table.sort</c> with the reference algorithm.</summary>
    public const string Install = """
        table.sort = (function()
        local function refsort(t, comp)
          local lt = comp or function(a, b) return a < b end
          local function aux(lo, up)
            while lo < up do
              if lt(t[up], t[lo]) then t[lo], t[up] = t[up], t[lo] end
              if up - lo == 1 then return end
              local p = math.floor((lo + up) / 2)
              if lt(t[p], t[lo]) then
                t[p], t[lo] = t[lo], t[p]
              elseif lt(t[up], t[p]) then
                t[p], t[up] = t[up], t[p]
              end
              if up - lo == 2 then return end
              local pivot = t[p]
              t[p], t[up - 1] = t[up - 1], t[p]
              local i, j = lo, up - 1
              while true do
                i = i + 1
                while lt(t[i], pivot) do
                  if i == up - 1 then error('invalid order function for sorting') end
                  i = i + 1
                end
                j = j - 1
                while lt(pivot, t[j]) do
                  if j < i then error('invalid order function for sorting') end
                  j = j - 1
                end
                if j < i then break end
                t[i], t[j] = t[j], t[i]
              end
              t[up - 1], t[i] = t[i], t[up - 1]
              if i - lo < up - i then
                aux(lo, i - 1)
                lo = i + 1
              else
                aux(i + 1, up)
                up = i - 1
              end
            end
          end
          aux(1, #t)
        end
        return refsort
        end)()
        """;
}
