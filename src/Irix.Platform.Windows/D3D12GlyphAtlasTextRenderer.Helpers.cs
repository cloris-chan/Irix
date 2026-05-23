using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Irix.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
    private static void RequirePointer(void* pointer, string message)
    {
        if (pointer == null)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static DWRITE_FONT_WEIGHT ToDirectWriteFontWeight(TextFontWeight fontWeight)
    {
        return fontWeight switch
        {
            TextFontWeight.Bold => DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_BOLD,
            TextFontWeight.SemiBold => DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD,
            _ => DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL
        };
    }

    private static DWRITE_FONT_STYLE ToDirectWriteFontStyle(TextFontStyle fontStyle)
    {
        return fontStyle switch
        {
            TextFontStyle.Italic => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_ITALIC,
            TextFontStyle.Oblique => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_OBLIQUE,
            _ => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL
        };
    }

    private static DWRITE_FONT_STRETCH ToDirectWriteFontStretch(TextFontStretch fontStretch)
    {
        return DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL;
    }

    private static string ToDirectWriteFontFamily(TextFontFamily fontFamily)
    {
        return fontFamily switch
        {
            TextFontFamily.SegoeUi => "Segoe UI",
            _ => "Segoe UI"
        };
    }

    private static DWRITE_READING_DIRECTION DetermineParagraphReadingDirection(ReadOnlySpan<char> text) =>
        TryDetermineRangeReadingDirection(text, 0, text.Length, out var direction) ? direction : DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT;

    private static bool TryDetermineRangeReadingDirection(ReadOnlySpan<char> text, int start, int end, out DWRITE_READING_DIRECTION direction)
    {
        var clampedStart = Math.Clamp(start, 0, text.Length);
        var clampedEnd = Math.Clamp(end, clampedStart, text.Length);
        for (var i = clampedStart; i < clampedEnd; i++)
        {
            if (TryGetStrongReadingDirection(text[i], out direction))
            {
                return true;
            }
        }

        direction = DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT;
        return false;
    }

    private static bool TryGetStrongReadingDirection(char character, out DWRITE_READING_DIRECTION direction)
    {
        if (GlyphAtlasTextCompositionHelpers.IsRightToLeftStrongCharacter(character))
        {
            direction = DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT;
            return true;
        }

        if (char.IsLetter(character))
        {
            direction = DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT;
            return true;
        }

        direction = DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var fontFace in _fontFaces.Values)
        {
            fontFace.Release();
        }

        foreach (var fontFace in _fallbackFontFaces.Values)
        {
            fontFace.Release();
        }

        for (var i = 0; i < _atlasPages.Count; i++)
        {
            _atlasPages[i].Release();
        }
        _atlasPages.Clear();

        for (var i = 0; i < _vbufs.Length; i++)
        {
            if (_vbufs[i] != null)
            {
                _vbufs[i]->Release();
                _vbufs[i] = null;
            }
        }
        if (_bgraPso != null) _bgraPso->Release();
        if (_pso != null) _pso->Release();
        if (_rootSig != null) _rootSig->Release();
        ReleaseWicFactory();
        if (_fontFallback != null) _fontFallback->Release();
        if (_textAnalyzer != null) _textAnalyzer->Release();
        if (_fontCollection != null) _fontCollection->Release();
        if (_dwriteFactory4 != null) _dwriteFactory4->Release();
        if (_dwriteFactory != null) _dwriteFactory->Release();
        _disposed = true;
    }

}

