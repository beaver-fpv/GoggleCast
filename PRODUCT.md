# 观幕 GoggleCast

<!-- impeccable:product-schema 1 -->

## Platform

Native Windows desktop. This is not a website or a mobile app.

## Users

DJI Goggles 3 owners who want to view Windows desktop content and videos in the goggles without a phone acting as an intermediary.

## Product Purpose

Select a screen or video, find a local DLNA renderer, and start or stop playback in the goggles. Make connection state and failure recovery understandable in the user's selected language.

## Stack

Implementation choice: C# with Windows desktop UI and the installed .NET Framework, FFmpeg for video encoding, SSDP discovery and UPnP AVTransport. No new SDK or system driver installation required. A per-user Windows installer is the primary distribution; a portable package remains available.

## Capabilities and Constraints

- Windows to Goggles 3 is the direction. It is separate from the user's existing USB goggles-to-PC viewer.
- Goggles must enable Wireless Streaming and share a LAN with the computer.
- Screen streaming and local video playback are the first version's focus.
- Desktop sharing includes the default Windows playback device through WASAPI loopback. Audio and video share a timeline and are sent together; the user listens through the goggles' headphones. This does not compensate sound heard directly from the computer.
- The user requested multiple languages. The initial UI provides Simplified Chinese, Traditional Chinese, English, Japanese, Korean, Spanish, French and German, with a persistent language selector and system-language default.
- Receiver buffering can add seconds. Do not claim gaming latency or fabricate hardware validation.
- Actual goggles playback requires the user's powered device and an on-device check. Local transport, encoding and error behavior can be tested independently.
- Do not install firewall rules or change network settings silently.

## Product Principles

- Show what has been verified: encoding, device request, transport status, and actual image confirmation are different states.
- Defaults should make ordinary video viewing easy.
- Use readable Chinese text and familiar Windows controls.
- Starting a stream is explicit; startup does not capture the screen.

## Evidence on Hand

DLNA desktop/video reception can involve several-second receiver buffering. USB goggles-to-PC live-view latency measurements concern the opposite direction and do not measure this software.

On 2026-10-18 the user's Goggles 3 displayed the moving test picture, 1080p desktop and transcoded video over the PC's mobile hotspot. The user separately confirmed perceptually synchronized video/audio and desktop/system audio through goggles headphones. Precise latency and long-duration drift were not measured.
