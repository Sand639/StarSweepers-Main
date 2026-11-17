// ゴールに常に出ている黒い渦（SpaceJunkGoalVortex）。説明は Documents/機能ドキュメント/ゴールの渦.md
// 床に寝かせた板に、上から見て左回りに回る黒い渦を描く。真ん中ほど暗く、ふちはチームの色でうっすら光る。
// 黒い所は下の床を暗くし（アルファ）、光る所は色を足す重ね方（Blend One OneMinusSrcAlpha）。
Shader "StarSweepers/GoalVortex"
{
    Properties
    {
        _Color ("Color (ふちの色。スクリプトがチームの色を入れる)", Color) = (0.3, 0.6, 0.95, 1)
        _VortexTime ("Vortex Time (時刻。スクリプトが入れる)", Float) = 0
        _Appear ("Appear (出ている量 0-1。スクリプトが入れる)", Range(0, 1)) = 1
        _Brightness ("Brightness (ふちの光の明るさ。スクリプトが入れる)", Range(0, 4)) = 1
        _Arms ("Arms (渦の腕の数)", Range(1, 8)) = 4
        _Twist ("Twist (腕の巻き具合)", Range(0, 12)) = 5
        _Speed ("Speed (回る速さ 回転/秒)", Range(0, 2)) = 0.35
        _Darkness ("Darkness (渦の黒さ)", Range(0, 1)) = 0.96
        _RimWidth ("Rim Width (ふちの光の太さ 0-1)", Range(0.02, 0.5)) = 0.07
        _Specks ("Specks (吸い込まれていく光の粒の濃さ)", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GoalVortex"
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
                float _Arms;
                float _Twist;
                float _Speed;
                float _Darkness;
                float _RimWidth;
                float _Specks;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0; // 真ん中が 0、ふちが ±1
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float r = length(input.uv);
                if (r > 1.0)
                {
                    return 0;
                }

                // 出てくるときは、真ん中から外へ広がる
                float appear = saturate(_Appear);
                float reach = appear * 1.15;
                float shown = saturate((reach - r) / 0.15);
                if (shown <= 0.0)
                {
                    return 0;
                }

                float angle = atan2(input.uv.y, input.uv.x);
                float spin = _VortexTime * _Speed * 6.2831853;

                // 渦の腕：対数の渦巻き。上から見て左回りに回り、真ん中へ巻き込む
                float phase = angle * _Arms - _Twist * log(max(r, 0.02)) * 2.0 - spin * _Arms;
                float arms = 0.5 + 0.5 * sin(phase);
                arms = smoothstep(0.15, 0.95, arms);

                // 黒さ：全体をほぼ真っ黒にし、真ん中ほど濃くする。ふちの近くだけ少し薄くして、床へつなぐ
                float core = 1.0 - smoothstep(0.0, 0.35, r);
                float body = lerp(0.84, 1.0, arms) * (1.0 - smoothstep(0.82, 1.0, r) * 0.45);
                float alpha = saturate(max(body, core)) * _Darkness;

                // ふちの光：外のふちでチームの色にうっすら光る。腕の間にも、ごく薄く色をのせて渦の筋を見せる
                float rim = smoothstep(1.0 - _RimWidth * 2.0, 1.0 - _RimWidth * 0.5, r) * (1.0 - smoothstep(0.97, 1.0, r));
                float streak = (1.0 - arms) * smoothstep(0.2, 0.95, r) * 0.22;

                // 吸い込まれていく光の粒：ふちから真ん中へ、回りながら流れる
                float lanes = 18.0;
                float lane = floor((angle / 6.2831853 + 0.5) * lanes + r * 3.0);
                float seed = Hash(float2(lane, 7.0));
                float flow = frac(r * 1.6 + _VortexTime * (0.45 + seed * 0.4) + seed);
                float laneCenter = frac((angle / 6.2831853 + 0.5) * lanes + r * 3.0);
                float speck = smoothstep(0.08, 0.0, abs(flow - 0.5) * 0.6) * smoothstep(0.35, 0.0, abs(laneCenter - 0.5)) * step(0.45, seed);
                speck *= smoothstep(0.15, 0.4, r) * _Specks;

                float glow = (rim * 0.9 + streak + speck) * _Brightness;
                half3 color = _Color.rgb * glow;

                // 外側のふちは、床へなめらかに溶かす
                float edge = 1.0 - smoothstep(0.96, 1.0, r);
                alpha *= edge * shown;
                color *= shown;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
