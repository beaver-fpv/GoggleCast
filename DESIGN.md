# 观幕 GoggleCast — Windows 工具界面

GoggleCast is a native Windows desktop application for choosing content, finding a local receiver and controlling a casting session.

Use native Windows controls with Microsoft YaHei UI for readable Chinese. A deep ink header and blue primary action anchor a quiet, pale workspace. Source settings and receiver selection are the main work areas. Persistent session status, explicit start/stop actions and an expandable log complete the workflow. Normal size is 1100 × 760, minimum is 900 × 650.

The layout prioritizes readable hierarchy, task grouping and familiar Windows controls. Source settings and device selection stay visually distinct, while session state and the start/stop controls remain available as the center region scrolls.

No decorative imagery. No fake discovered devices, progress percentages, measured latency or claims that goggles are displaying an image. Display transport requests, bytes served and receiver state separately. Errors state the problem and an actionable next step. Capture is never started merely by launching the app.

Implemented colors: foreground #1A2940, secondary #53657C, canvas #F3F5F8, surface #FFFFFF, header #142840, action #165DDE, error #A0351B. Type scale: title 26, section 20, controls/body 14, helper 13. Use visible keyboard focus, 36–44 px control heights, 8/12/16/24 spacing and no narrow fixed-width status text.

Verify the native interface with rendered screenshots and actual control interaction, including the minimum window size and connection failure states.

Language support uses original Chinese message keys and complete Traditional Chinese, English, Japanese, Korean, Spanish, French and German dictionaries. The selector shows language self-names, changes the current view immediately, and saves to settings.xml beside the executable. Japanese uses Yu Gothic UI and Korean uses Malgun Gothic; other locales use Microsoft YaHei UI with Windows fallback. Technical logs retain original device/FFmpeg text. The center region scrolls at the 900 × 650 minimum size while Start/Stop and status remain visible. Rendered checks cover all eight languages, German/French long text at minimum width, translated error state and restart persistence.
