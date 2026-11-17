// ゴールの演出「光の柱」（GoalEffect の見た目の種類）。説明は Documents/機能ドキュメント/ゴールの渦と光の柱.md
// 4つの部品を1つのシェーダーで描く（どれを描くかは、スクリプトが _Part で渡す）。
//   0 = 床の光 … ゴールの床が丸く明るく光り、筒の外へもやのようににじむ
//   1 = 筒 … うすく光る半透明の筒。上へ行くほど消える。縦の光の筋が下から上へ流れる
//   2 = 光の筋 … 筒の中に立つ細長い光（カメラのほうを向く板）。下から上へ流れる
//   3 = きらめき … 小さな十字の光。筒の中から上へ立ちのぼり、筒より上まで漂って消える
// 色を足して光らせる。下の床も少しだけ暗くして（アルファ）、明るい床の上でも色が白に飛びにくくする（Blend One OneMinusSrcAlpha）。
Shader "StarSweepers/GoalPillar"
{
    Properties
    {
        _Color ("Color (光の色。スクリプトがチームの色などを入れる)", Color) = (0.35, 1, 0.55, 1)
        _VortexTime ("Vortex Time (時刻。スクリプトが入れる)", Float) = 0
        _Appear ("Appear (出ている量 0-1。スクリプトが入れる)", Range(0, 1)) = 1
        _Brightness ("Brightness (明るさ。スクリプトが入れる)", Range(0, 4)) = 1.2
        _Part ("Part (0=床 1=筒 2=筋 3=きらめき。スクリプトが入れる)", Float) = 0
        _Funnel ("Funnel (x=筒の半径 m、y=筒の高さ m。スクリプトが入れる)", Vector) = (1.8, 1.5, 0, 0)
        _WallGlow ("Wall Glow (筒の光の濃さ)", Range(0, 2)) = 1
        _FloorGlow ("Floor Glow (床の光の濃さ)", Range(0, 2)) = 0.8
        _StreakGlow ("Streak Glow (光の筋の濃さ)", Range(0, 2)) = 1
        _SparkleGlow ("Sparkle Glow (きらめきの濃さ)", Range(0, 3)) = 1.4
        _SparkleSize ("Sparkle Size (きらめきの大きさ m)", Range(0.02, 0.6)) = 0.16
        _RiseSpeed ("Rise Speed (筋ときらめきがのぼる速さ)", Range(0.05, 3)) = 0.6
        _Cover ("Cover (下の床を暗くする強さ)", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GoalPillar"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _VortexTime;
                float _Appear;
                float _Brightness;
                float _Part;
                float4 _Funnel;
                float _WallGlow;
                float _FloorGlow;
                float _StreakGlow;
                float _SparkleGlow;
                float _SparkleSize;
                float _RiseSpeed;
                float _Cover;
            CBUFFER_END

            #define TAU 6.2831853

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;   // 床：真ん中 0・ふち ±1／板：角 −1〜1
                float2 seed : TEXCOORD1; // 板ごとの乱数
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 local : TEXCOORD1;
                float2 seed : TEXCOORD2;
                float fade : TEXCOORD3;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.uv = input.uv;
                output.local = input.positionOS.xyz;
                output.seed = input.seed;
                output.fade = 1;

                float3 cameraRight = UNITY_MATRIX_V[0].xyz;
                float3 cameraUp = UNITY_MATRIX_V[1].xyz;

                if (_Part > 2.5)
                {
                    // ---- きらめき：筒の中から、筒より上（高さの 1.7 倍）までのぼって消える ----
                    float phase = frac(_VortexTime * _RiseSpeed * (0.35 + 0.4 * input.seed.x) + input.seed.y);
                    float sway = sin(_VortexTime * 1.3 + input.seed.y * TAU) * 0.08;
                    float3 root = float3(input.positionOS.x * (1.0 - 0.25 * phase) + sway, phase * 1.7, input.positionOS.z * (1.0 - 0.25 * phase) - sway);
                    float3 centerWS = TransformObjectToWorld(root);
                    float twinkle = 0.7 + 0.3 * sin(_VortexTime * 9.0 + input.seed.x * 40.0);
                    output.fade = smoothstep(0.0, 0.12, phase) * (1.0 - smoothstep(0.65, 1.0, phase)) * twinkle;
                    float size = _SparkleSize * (0.6 + 0.8 * input.seed.x) * saturate(_Appear);
                    float3 positionWS = centerWS + (cameraRight * input.uv.x + cameraUp * input.uv.y) * size;
                    output.positionHCS = TransformWorldToHClip(positionWS);
                    return output;
                }

                if (_Part > 1.5)
                {
                    // ---- 光の筋：筒の高さいっぱいの細い板。横はカメラのほうを向く ----
                    float v = input.uv.y * 0.5 + 0.5;
                    output.local = float3(input.positionOS.x, v, input.positionOS.z);
                    float3 baseWS = TransformObjectToWorld(float3(input.positionOS.x, v * 1.05, input.positionOS.z));
                    float3 side = normalize(float3(cameraRight.x, 0.0, cameraRight.z) + 1e-5);
                    float width = 0.03 * (0.6 + 0.9 * input.seed.x);
                    float3 positionWS = baseWS + side * input.uv.x * width;
                    output.positionHCS = TransformWorldToHClip(positionWS);
                    return output;
                }

                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            // 出す色（光る分）と、下を暗くする量
            half4 Glow(float intensity, float cover)
            {
                float appear = saturate(_Appear);
                float3 color = _Color.rgb * intensity * _Brightness * appear;
                return half4(color, saturate(cover * _Cover * appear));
            }

            // 床の光：筒の内側は丸く明るく、ふちに明るい輪、外へはもやのようににじむ
            half4 FloorPart(Varyings input)
            {
                float r = length(input.uv) * 1.35; // 1 が筒のふち
                if (r > 1.35)
                {
                    return 0;
                }
                float inside = 1.0 - smoothstep(0.0, 1.0, r);
                float ring = exp(-abs(r - 1.0) * 14.0);
                float haze = (1.0 - smoothstep(1.0, 1.35, r)) * 0.25;
                float pulse = 0.85 + 0.15 * sin(_VortexTime * 2.2);
                float intensity = (inside * 0.55 * pulse + ring * 0.9 + haze) * _FloorGlow;
                return Glow(intensity, inside * 0.8 + ring * 0.5);
            }

            // 筒：上へ行くほど消える。縦の光の筋が下から上へ流れる
            half4 WallPart(Varyings input)
            {
                float v = saturate(input.local.y);
                float angle = atan2(input.local.z, input.local.x);
                float column = floor((angle / TAU + 0.5) * 36.0);
                float seed = Hash(float2(column, 3.0));
                float flow = frac(v * 0.9 - _VortexTime * _RiseSpeed * (0.6 + seed) + seed * 5.0);
                float dash = smoothstep(0.0, 0.08, flow) * (1.0 - smoothstep(0.25, 0.5, flow)) * step(0.45, seed);
                float within = frac((angle / TAU + 0.5) * 36.0);
                float stripe = exp(-abs(within - 0.5) * 18.0);

                float fadeUp = pow(1.0 - v, 1.6);
                float bottomEdge = exp(-v * 18.0);
                float intensity = (fadeUp * 0.7 + bottomEdge * 0.7 + dash * stripe * 1.1 * (1.0 - v)) * _WallGlow;
                return Glow(intensity, fadeUp * 0.45);
            }

            // 光の筋：細い光の線が、下から上へ流れる
            half4 StreakPart(Varyings input)
            {
                float v = input.local.y;
                float across = 1.0 - input.uv.x * input.uv.x;
                float flow = frac(v * 0.7 - _VortexTime * _RiseSpeed * (0.8 + input.seed.x) + input.seed.y * 3.0);
                float dash = smoothstep(0.0, 0.1, flow) * (1.0 - smoothstep(0.3, 0.6, flow));
                float fadeUp = 1.0 - smoothstep(0.55, 1.0, v);
                float intensity = across * across * dash * fadeUp * _StreakGlow;
                return Glow(intensity, 0.0);
            }

            // きらめき：十字の光と、小さく白っぽい芯
            half4 SparklePart(Varyings input)
            {
                float2 p = input.uv;
                float star = exp(-abs(p.x) * 16.0) * exp(-abs(p.y) * 2.6) + exp(-abs(p.y) * 16.0) * exp(-abs(p.x) * 2.6);
                float core = exp(-dot(p, p) * 12.0);
                float intensity = (star * 0.9 + core) * input.fade * _SparkleGlow;
                half4 glow = Glow(intensity, 0.0);
                glow.rgb += core * input.fade * 0.35 * saturate(_Appear);
                return glow;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_Part > 2.5) return SparklePart(input);
                if (_Part > 1.5) return StreakPart(input);
                if (_Part > 0.5) return WallPart(input);
                return FloorPart(input);
            }
            ENDHLSL
        }
    }
}
