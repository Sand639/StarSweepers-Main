// ゴールに常に出ている黒い渦（SpaceJunkGoalVortex）。説明は Documents/機能ドキュメント/ゴールの渦と光の柱.md
// 4つの部品を1つのシェーダーで描く（どれを描くかは、スクリプトが _Part で渡す）。
//   0 = 床の渦 … 床に寝かせた板に、上から見て左回りに回る黒い渦（すり鉢の底）
//   1 = すり鉢の壁 … 上が広く、床へ向かって細くなる半透明の筒。黒い筋が回りながら下へ流れ込む
//   2 = らせんの帯 … すり鉢に沿って巻きつく細い帯。帯ごとに違う速さで回り、端はかすれて消える
//   3 = かけら … 黒い小さな破片。らせんを描いて回りながら、すり鉢の底へ吸い込まれていく
// **光らせない**：色は _Color（灰色。チームの色にもできる）より暗い色だけを使い、アルファの分だけ下を隠す重ね方（Blend One OneMinusSrcAlpha で、色にアルファを掛けて出す）。
Shader "StarSweepers/GoalVortex"
{
    Properties
    {
        _Color ("Color (明るい所の色。スクリプトが入れる。ふだんは灰色)", Color) = (0.55, 0.55, 0.58, 1)
        _VortexTime ("Vortex Time (時刻。スクリプトが入れる)", Float) = 0
        _Appear ("Appear (出ている量 0-1。スクリプトが入れる)", Range(0, 1)) = 1
        _Brightness ("Brightness (明るい所の明るさ。スクリプトが入れる)", Range(0, 2)) = 1
        _Part ("Part (0=床 1=壁 2=帯 3=かけら。スクリプトが入れる)", Float) = 0
        _Funnel ("Funnel (x=底の半径の割合 y=曲がり具合 z=渦の半径 m w=すり鉢の高さ m。スクリプトが入れる)", Vector) = (0.3, 1.8, 1.8, 1.6)
        _Arms ("Arms (床の渦の腕の数)", Range(1, 8)) = 4
        _Twist ("Twist (床の渦の腕の巻き具合)", Range(0, 12)) = 5
        _Speed ("Speed (回る速さ 回転/秒)", Range(0, 2)) = 0.35
        _Darkness ("Darkness (渦の黒さ)", Range(0, 1)) = 0.96
        _RimWidth ("Rim Width (床の渦のふちの太さ 0-1)", Range(0.02, 0.5)) = 0.07
        _FunnelTwist ("Funnel Twist (すり鉢の筋の巻き具合)", Range(0, 20)) = 7
        _FunnelOpacity ("Funnel Opacity (すり鉢の壁の濃さ)", Range(0, 1)) = 0.45
        _RibbonOpacity ("Ribbon Opacity (らせんの帯の濃さ)", Range(0, 1)) = 0.95
        _DebrisSize ("Debris Size (かけらの大きさ m)", Range(0.01, 0.4)) = 0.12
        _DebrisSpeed ("Debris Speed (かけらが底へ着くまでの回数/秒)", Range(0.05, 2)) = 0.35
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
                float _Part;
                float4 _Funnel;
                float _Arms;
                float _Twist;
                float _Speed;
                float _Darkness;
                float _RimWidth;
                float _FunnelTwist;
                float _FunnelOpacity;
                float _RibbonOpacity;
                float _DebrisSize;
                float _DebrisSpeed;
            CBUFFER_END

            #define TAU 6.2831853

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;   // 床：真ん中 0・ふち ±1／帯：x=長さ方向 0-1、y=幅方向 0-1
                float2 seed : TEXCOORD1; // 帯：x=回る速さ、y=帯の番号の乱数／かけら：乱数2つ
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 local : TEXCOORD1;  // 壁：形の中の位置（y が高さ 0-1）
                float2 seed : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float fade : TEXCOORD4;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // すり鉢の高さ v（0-1）での半径（上のふちを 1 とした割合）
            float FunnelRadius(float v)
            {
                return _Funnel.x + (1.0 - _Funnel.x) * pow(saturate(v), _Funnel.y);
            }

            float3 RotateY(float3 p, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float3(c * p.x - s * p.z, p.y, s * p.x + c * p.z);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.uv = input.uv;
                output.local = input.positionOS.xyz;
                output.seed = input.seed;
                output.normalWS = float3(0, 1, 0);
                output.fade = 1;
                float3 position = input.positionOS.xyz;

                if (_Part > 2.5)
                {
                    // ---- かけら：決まった道（らせん）を、回りながら底へ下りる。形の中の位置は m ----
                    float phase = frac(_VortexTime * _DebrisSpeed * (0.7 + 0.6 * input.seed.x) + input.seed.y);
                    float v = 1.0 - phase;
                    float radius = FunnelRadius(v) * _Funnel.z * (0.92 + 0.16 * input.seed.x);
                    float angle = input.seed.y * TAU + phase * phase * 2.5 * TAU;
                    float3 center = float3(cos(angle) * radius, v * _Funnel.w + 0.05, sin(angle) * radius);

                    // くるくる回る（自分の向き）
                    float tumble = _VortexTime * (2.0 + 3.0 * input.seed.x) + input.seed.y * TAU;
                    float3 corner = RotateY(input.positionOS.xyz, tumble);
                    float3 normal = RotateY(input.normalOS, tumble);
                    float s1 = sin(tumble * 0.7);
                    float c1 = cos(tumble * 0.7);
                    corner = float3(corner.x, c1 * corner.y - s1 * corner.z, s1 * corner.y + c1 * corner.z);
                    normal = float3(normal.x, c1 * normal.y - s1 * normal.z, s1 * normal.y + c1 * normal.z);

                    // 底へ近づくほど小さく。出てすぐと、底に着く直前は消える
                    float size = _DebrisSize * (0.6 + 0.8 * input.seed.x) * (1.0 - 0.6 * phase) * saturate(_Appear);
                    output.fade = smoothstep(0.0, 0.08, phase) * (1.0 - smoothstep(0.88, 1.0, phase));
                    position = center + corner * size * output.fade;
                    output.normalWS = TransformObjectToWorldNormal(normal);
                }
                else if (_Part > 1.5)
                {
                    // ---- らせんの帯：帯ごとの速さで、すり鉢のまわりを回る ----
                    position = RotateY(position, _VortexTime * input.seed.x * TAU);
                }

                output.positionHCS = TransformObjectToHClip(position);
                return output;
            }

            // 床の渦
            half4 Floor(Varyings input)
            {
                float r = length(input.uv);
                if (r > 1.0)
                {
                    return 0;
                }

                float appear = saturate(_Appear);
                float shown = saturate((appear * 1.15 - r) / 0.15);
                if (shown <= 0.0)
                {
                    return 0;
                }

                float angle = atan2(input.uv.y, input.uv.x);
                float spin = _VortexTime * _Speed * TAU;
                float phase = angle * _Arms - _Twist * log(max(r, 0.02)) * 2.0 - spin * _Arms;
                float arms = smoothstep(0.15, 0.95, 0.5 + 0.5 * sin(phase));

                float core = 1.0 - smoothstep(0.0, 0.35, r);
                float body = lerp(0.84, 1.0, arms) * (1.0 - smoothstep(0.82, 1.0, r) * 0.45);
                float alpha = saturate(max(body, core)) * _Darkness;

                // 明るい所（灰色）：腕の間と、外のふち。真ん中は真っ黒
                float rim = smoothstep(1.0 - _RimWidth * 2.0, 1.0 - _RimWidth * 0.5, r) * (1.0 - smoothstep(0.97, 1.0, r));
                float gray = saturate((1.0 - arms) * smoothstep(0.2, 0.95, r) * 0.35 + rim * 0.7) * _Brightness;
                float3 color = _Color.rgb * gray;

                alpha *= (1.0 - smoothstep(0.96, 1.0, r)) * shown;
                return half4(color * alpha, alpha);
            }

            // すり鉢の壁
            half4 Funnel(Varyings input)
            {
                float v = saturate(input.local.y);
                float angle = atan2(input.local.z, input.local.x);
                float spin = _VortexTime * _Speed * TAU;
                float phase = angle * _Arms - v * _FunnelTwist - spin * _Arms * 1.5;
                float band = smoothstep(0.45, 0.95, 0.5 + 0.5 * sin(phase));

                float topFade = 1.0 - smoothstep(0.6, 1.0, v);
                float alpha = (0.25 + 0.45 * (1.0 - v) + band * 0.4) * topFade * _FunnelOpacity * _Darkness * saturate(_Appear);
                float gray = (1.0 - band) * 0.3 * _Brightness;
                return half4(_Color.rgb * gray * alpha, saturate(alpha));
            }

            // らせんの帯：黒い帯と灰色の帯。幅の両側と、長さの両端はかすれて消える
            half4 Ribbon(Varyings input)
            {
                float along = input.uv.x;
                float across = abs(input.uv.y * 2.0 - 1.0);
                float speed = input.seed.x;
                float id = input.seed.y;

                float ends = smoothstep(0.0, 0.18, along) * (1.0 - smoothstep(0.7, 1.0, along));
                float edge = 1.0 - smoothstep(0.35, 1.0, across);

                // かすれ：長さ方向に流れる、すき間のある模様
                float flow = along * 14.0 - _VortexTime * (1.5 + speed * 6.0) + id * 17.0;
                float scratch = 0.5 + 0.5 * sin(flow) * sin(flow * 0.37 + across * 3.0);
                float grain = Hash(float2(floor(along * 60.0 - _VortexTime * 20.0 * speed), floor(across * 4.0) + id * 9.0));
                float worn = smoothstep(0.1, 0.45, scratch) * lerp(0.7, 1.0, grain);

                float alpha = ends * edge * worn * _RibbonOpacity * saturate(_Appear);
                float gray = (frac(id * 7.31) > 0.5 ? 0.7 : 0.08) * _Brightness;
                return half4(_Color.rgb * gray * alpha, saturate(alpha));
            }

            // かけら：黒〜暗い灰色の固い物（光は当てず、向きで明るさを少し変えるだけ）
            half4 Debris(Varyings input)
            {
                float facing = saturate(dot(normalize(input.normalWS), normalize(float3(0.35, 1.0, -0.4))));
                float gray = lerp(0.04, 0.38, facing) * _Brightness;
                float alpha = saturate(input.fade * 4.0) * saturate(_Appear);
                return half4(_Color.rgb * gray * alpha, alpha);
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_Part > 2.5) return Debris(input);
                if (_Part > 1.5) return Ribbon(input);
                if (_Part > 0.5) return Funnel(input);
                return Floor(input);
            }
            ENDHLSL
        }
    }
}
