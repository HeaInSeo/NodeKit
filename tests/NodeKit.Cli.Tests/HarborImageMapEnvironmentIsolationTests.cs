using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// #109 / S4-05-A: NODEKIT_HARBOR_IMAGE_MAP를 바꾸는 test class가 공유 non-parallel collection에서 빠지면
    /// 경합은 타이밍이 맞을 때만 실패한다(CI 반복 실행 20회도 놓칠 수 있다). 그래서 배선 자체를 결정적으로 확인한다.
    /// 소스 문자열 대신 컴파일된 IL의 ldstr를 보므로 상수, 줄바꿈, lambda/async, static 필드 초기화 모두 같은 결과다.
    /// </summary>
    public class HarborImageMapEnvironmentIsolationTests
    {
        private const string MapVariable = "NODEKIT_HARBOR_IMAGE_MAP";
        private const string Sentinel = "NODEKIT_ISOLATION_GUARD_SENTINEL";

        private static readonly Dictionary<ushort, OpCode> _opCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => unchecked((ushort)o.Value));

        [Fact]
        public void CollectionDefinition_DisablesParallelization()
        {
            var definition = typeof(HarborImageMapEnvironmentCollection).GetCustomAttributesData()
                .Single(a => a.AttributeType == typeof(CollectionDefinitionAttribute));

            Assert.Equal(nameof(HarborImageMapEnvironmentCollection), definition.ConstructorArguments.Single().Value);
            var disable = definition.NamedArguments.Single(a => a.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization));
            Assert.Equal(true, disable.TypedValue.Value);
        }

        [Fact]
        public void EveryClassThatUsesTheMap_IsInTheSharedCollection()
        {
            var owners = TypesLoadingString(typeof(HarborImageMapEnvironmentIsolationTests).Assembly, MapVariable)
                .Where(t => t != typeof(HarborImageMapEnvironmentIsolationTests))
                .ToArray();

            // 스캔이 비어서 통과하는 일이 없도록 알려진 두 class가 잡히는지 먼저 본다.
            Assert.Contains(typeof(HarborBaseImageSelectionTests), owners);
            Assert.Contains(typeof(MappedHarborImageDigestResolverTests), owners);

            // 이 env를 읽거나 바꾸는 class는 모두 같은 collection에 있어야 한다.
            foreach (var type in owners)
            {
                var collection = type.GetCustomAttributesData()
                    .SingleOrDefault(a => a.AttributeType == typeof(CollectionAttribute));
                Assert.True(collection is not null, $"{type.FullName}에 [Collection(nameof(HarborImageMapEnvironmentCollection))]이 없습니다.");
                Assert.Equal(nameof(HarborImageMapEnvironmentCollection), collection!.ConstructorArguments.Single().Value);
            }
        }

        [Fact]
        public void Scanner_FindsTheNameRegardlessOfSourceFormatting()
        {
            var owners = TypesLoadingString(typeof(HarborImageMapEnvironmentIsolationTests).Assembly, Sentinel)
                .Where(t => t != typeof(HarborImageMapEnvironmentIsolationTests))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    typeof(SentinelViaAsyncMethod),
                    typeof(SentinelViaConstant),
                    typeof(SentinelViaLambda),
                    typeof(SentinelViaNextLineLiteral),
                    typeof(SentinelViaStaticField),
                },
                owners);
        }

        // ldstr로 value를 싣는 method를 가진 type을 모은다. compiler-generated type(lambda closure, async state machine)은
        // 그것을 선언한 type으로 올려 보고한다.
        private static HashSet<Type> TypesLoadingString(Assembly assembly, string value)
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var owners = new HashSet<Type>();
            foreach (var type in assembly.GetTypes())
            {
                var methods = type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All));
                if (methods.Any(m => LoadsString(m, value)))
                {
                    owners.Add(DeclaringUserType(type));
                }
            }

            return owners;
        }

        private static Type DeclaringUserType(Type type)
        {
            while (type.DeclaringType is not null && type.IsDefined(typeof(CompilerGeneratedAttribute), false))
            {
                type = type.DeclaringType;
            }

            return type;
        }

        private static bool LoadsString(MethodBase method, string value)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null)
            {
                return false;
            }

            var i = 0;
            while (i < il.Length)
            {
                ushort code = il[i++];
                if (code == 0xFE)
                {
                    code = (ushort)(0xFE00 | il[i++]);
                }

                var opCode = _opCodesByValue[code];
                if (opCode == OpCodes.Ldstr)
                {
                    if (method.Module.ResolveString(BitConverter.ToInt32(il, i)) == value)
                    {
                        return true;
                    }
                }

                i += OperandSize(opCode.OperandType, il, i);
            }

            return false;
        }

        private static int OperandSize(OperandType operandType, byte[] il, int at) => operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
            _ => 4,
        };

        // 아래 fixture는 실행하지 않는다. 같은 이름을 다른 소스 모양으로 쓰는 경우를 스캐너가 모두 잡는지 확인하는 용도다.
        private static class SentinelViaConstant
        {
            private const string Name = Sentinel;

            public static void Set() => Environment.SetEnvironmentVariable(Name, "x");
        }

        private static class SentinelViaNextLineLiteral
        {
            public static void Set() => Environment.SetEnvironmentVariable(
                "NODEKIT_ISOLATION_GUARD_SENTINEL",
                "x");
        }

        private static class SentinelViaLambda
        {
            public static Action Setter() => () => Environment.SetEnvironmentVariable(Sentinel, "x");
        }

        private static class SentinelViaAsyncMethod
        {
            public static async Task SetLaterAsync()
            {
                await Task.Delay(0).ConfigureAwait(false);
                Environment.SetEnvironmentVariable(Sentinel, "x");
            }
        }

        private static class SentinelViaStaticField
        {
            private static readonly string _name = Sentinel;

            public static void Set() => Environment.SetEnvironmentVariable(_name, "x");
        }
    }
}
