# Privacy Policy for Imvix

**Last Updated: March 2026**

**Version: 1.3.3**

---

## Introduction

Imvix ("we", "our", or "the Application") is a desktop image conversion utility developed by LPHYSQS. This Privacy Policy explains how our Application handles information when you use it.

**TL;DR: Imvix is a privacy-first, offline desktop application. We do not collect, transmit, or store any of your personal data on external servers. All processing happens locally on your device.**

---

## Information We Collect

### We Do NOT Collect:

- Personal identification information
- Usage analytics or telemetry data
- Crash reports or error logs
- Device or system information
- IP addresses or location data
- Cookies or tracking identifiers

### Local Data Storage

Imvix stores the following data **locally on your device** in the `%AppData%\Imvix` directory:

| File | Purpose |
|------|---------|
| `settings.json` | Your application preferences, conversion settings, presets, and window configuration |
| `history.json` | Recent conversion history (last 12 entries) for your convenience |
| `Logs/conversion-*.log` | Error logs generated only when batch conversions fail |

**Important:** This data never leaves your computer. It is used solely to provide application functionality and improve your user experience.

---

## Image Files

### How We Handle Your Images

- **Local Processing Only:** All image conversion operations are performed entirely on your local device. Your images are never uploaded to any external server or cloud service.
- **No Image Retention:** Imvix does not retain copies of your original or converted images beyond the duration of the conversion process.
- **User-Controlled Output:** Converted images are saved only to the output directory you specify. You have full control over where your files are stored.
- **Temporary Files:** Any temporary files created during processing (e.g., for GIF encoding) are stored in your system's temporary folder and are cleaned up after the conversion completes.

---

## Network Connectivity

Imvix operates entirely offline. The Application does not require an internet connection to perform its core functionality.

The only network-related features are:

- **Links in About Window:** The Application contains links to our official website and GitHub repository. These links are only activated when you explicitly click them, opening in your default web browser.

No data is transmitted through these links.

---

## Third-Party Services

Imvix does not integrate with any third-party analytics, advertising, or data collection services.

The Application uses the following libraries for local processing:

- **SkiaSharp** - Image processing
- **Svg.Skia** - SVG rendering
- **System.Drawing.Common** - GIF and TIFF encoding

These libraries operate entirely locally and do not transmit any data externally.

---

## Children's Privacy

Since Imvix does not collect any personal information, it is safe for users of all ages, including children under the age of 13. However, we recommend parental guidance for younger users when installing and configuring the Application.

---

## Data Security

Since all data is stored locally on your device, the security of your data depends on the security measures you have in place on your computer. We recommend:

- Keeping your operating system up to date
- Using appropriate security software
- Protecting your user account with a strong password

---

## Your Rights

Because Imvix does not collect or transmit any personal data, there is no data to access, modify, or delete from our servers. You have full control over your locally stored data:

- **View:** You can view your settings and history files in `%AppData%\Imvix`
- **Delete:** You can delete these files at any time to reset the Application
- **Export:** You can copy these files to backup your settings

---

## Changes to This Privacy Policy

We may update this Privacy Policy from time to time. Any changes will be reflected in the "Last Updated" date at the top of this document. We encourage you to review this Privacy Policy periodically.

---

## Open Source

Imvix is source-available software. You can review the complete source code on our GitHub repository:

**https://github.com/LPHYSQS/Imvix**

The source code transparency allows you to independently verify our privacy claims.

---

## Contact Us

If you have questions or concerns about this Privacy Policy, you may contact us through:

- **GitHub Issues:** https://github.com/LPHYSQS/Imvix/issues
- **Official Website:** https://lphysqs.github.io/ImvixWeb/

---

## Summary

| Aspect | Our Practice |
|--------|--------------|
| Data Collection | None |
| Telemetry | None |
| Analytics | None |
| Cloud Processing | None |
| Internet Required | No |
| Data Storage | Local only |
| Third-Party Sharing | None |

**Imvix is designed with privacy as a core principle. Your images and data remain yours.**
