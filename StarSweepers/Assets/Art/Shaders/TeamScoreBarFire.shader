// チームの得点バーの帯そのものを、画像を使わずアニメ風の炎にする UI 用シェーダー。
// 説明：Documents/スクリプトの説明/Game/UI/TeamScoreFlameEffect.md
Shader "StarSweepers/UI/TeamScoreBarFire"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture（使わない）", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Layout)]
        _BarRatio ("四角の下から何割が帯か（残りは炎が立ちのぼる所）", Range(0.1, 1)) = 0.5

        [Header(Tongues)]
        _TongueHeight ("ふだんの炎の舌の高さ", Range(0, 1.5)) = 0.5
        _TongueWidth ("炎の舌の太さ（ピクセル）", Range(4, 120)) = 18
        _Wobble ("ゆれの強さ", Range(0, 1.5)) = 0.6
        _Speed ("炎が上へ流れる速さ", Range(0, 6)) = 1.6
        _Depth ("帯の根元ほど明るくする強さ", Range(0, 1)) = 0.35

        [Header(Toon)]
        _InnerLevel ("明るい部分の始まり（大きいほど少ない）", Range(0, 1.5)) = 0.3
        _CoreLevel ("いちばん明るい部分の始まり", Range(0, 1.5)) = 0.58
        _InnerWhiten ("明るい部分の白さ", Range(0, 1)) = 0.35
        _CoreWhiten ("いちばん明るい部分の白さ", Range(0, 1)) = 0.75
        _RimWidth ("炎の舌のふちの線の太さ（0 で線なし）", Range(0, 0.3)) = 0.05
        _RimDarken ("ふちの線の暗さ（チームの色に掛ける。小さいほど暗い）", Range(0, 1)) = 0.5

        [Header(Boost)]
        _Boost ("演出の強さ（0=ふだん、1=得点が入った瞬間）", Range(0, 1)) = 0
        _BoostHeight ("演出で増える炎の高さ", Range(0, 2)) = 0.7
        _BoostBright ("演出で増える明るさ", Range(0, 1)) = 0.12

        // ---- ここから下は UI の標準シェーダーと同じ（マスク用） ----
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 uv            : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;

            float _BarRatio;
            float _TongueHeight;
            float _TongueWidth;
            float _Wobble;
            float _Speed;
            float _Depth;
            float _InnerLevel;
            float _CoreLevel;
            float _InnerWhiten;
            float _CoreWhiten;
            float _RimWidth;
            float _RimDarken;
            float _Boost;
            float _BoostHeight;
            float _BoostBright;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            // ---- ノイズ（画像を使わずに、なめらかなデコボコを作る） ----

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 大きいゆれと小さいゆれを重ねる（0〜1 くらい）
            float Fbm(float2 p)
            {
                float n = ValueNoise(p) * 0.6;
                n += ValueNoise(p * 2.1 + 7.3) * 0.3;
                n += ValueNoise(p * 4.3 + 1.7) * 0.1;
                return n;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;

                // 横 … Canvas の中のピクセル（帯の幅に関係なく、炎の舌の太さがそろう）
                float px = i.worldPosition.x;
                float v = i.uv.y;

                // 帯の上の端から上へ 0〜1（帯の中はマイナス）
                float above = (v - _BarRatio) / max(1.0 - _BarRatio, 0.0001);

                // ---- 炎の形 ----
                // 演出中は炎が高くなる
                float height = _TongueHeight * (1.0 + _Boost * _BoostHeight);

                // 上のほうほど左右にゆらす
                float sway = (Fbm(float2(px / (_TongueWidth * 3.0) + 5.0, above * 0.8 - t * 0.7)) - 0.5)
                             * _Wobble * saturate(above + 0.3);

                // 横は細かく・縦はゆっくり変わるノイズ → 縦に伸びた炎の舌になる。時間で上へ流れる
                float n = Fbm(float2(px / _TongueWidth + sway, above * 0.9 - t));

                // コントラストを強くして、舌と舌の間にすき間を作る
                n = saturate((n - 0.25) * 1.7);

                // 「炎の濃さ」。帯の上では上へ行くほど薄く、帯の中では下へ行くほど濃い（根元ほど明るい炎）
                float field = n * height * 1.3 - max(above, 0.0) * 0.6 + max(-above, 0.0) * _Depth;

                // 四角の上の端に近づいたら、炎を必ず細らせて消す（燃え上がっても、先が平らに切れないように）
                field -= smoothstep(0.7, 1.0, above) * 1.5;

                float aa = max(fwidth(field), 0.0001);

                // 帯の中は必ず炎。帯の上は濃さが 0 より大きい所だけ炎
                float alpha = above < 0.0 ? 1.0 : smoothstep(0.0, aa, field);

                // ---- 3 段の塗り分け（アニメ風）：外側＝チームの色、内側ほど白に近い ----
                float lit = field + _Boost * _BoostBright;
                float inner = smoothstep(_InnerLevel, _InnerLevel + aa, lit);
                float core = smoothstep(_CoreLevel, _CoreLevel + aa, lit);

                float3 baseColor = i.color.rgb;
                float3 col = baseColor;
                col = lerp(col, lerp(baseColor, 1.0, _InnerWhiten), inner);
                col = lerp(col, lerp(baseColor, 1.0, _CoreWhiten), core);

                // 炎の舌のふちに、チームの色を暗くした線（帯の上の部分だけ）
                float rimField = above < 0.0 ? 1.0 : field;
                float rim = (1.0 - smoothstep(_RimWidth, _RimWidth + aa, rimField)) * step(0.0001, _RimWidth);
                col = lerp(col, baseColor * _RimDarken, rim);

                fixed4 result = fixed4(col, alpha * i.color.a);

                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif

                return result;
            }
            ENDCG
        }
    }
}
