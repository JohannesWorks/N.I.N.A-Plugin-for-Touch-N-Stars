# Touch 'N' Stars

## Unreleased
- Persistent Celestia Atlas data (landscapes, DSS survey) now lives in `NINA\TnsCache` next to the other plugin data; an existing `NINA\Touch-N-Stars` tree is moved there once
- Atlas DSS survey download: new `/api/atlas/survey/*` endpoints (status, download, cancel, delete) fetch the DSS colour HiPS tile by tile onto the host as 512 px JPEG and serve it at `/celestia-atlas-data/surveys/dss` from the persistent data directory; resumable, one job at a time
- Atlas DSS survey delete now accepts an optional `keepOrder` to downgrade to a lower order instead of always wiping the whole survey
- Keep advertising the `_touchnstars._tcp` service and its active port on
  Linux while sharing hostname ownership with the system Avahi daemon.
- Package and serve the `celestia-atlas-data` application directory.
- Keep generated Celestia Atlas landscapes in persistent N.I.N.A. user data and
  migrate existing app-local landscapes without overwriting user content.
- Atlas NSNS narrowband survey (CC BY-NC-SA 4.0): the survey endpoints take an optional `survey` (`dss` default, `nsns` colour composite ohs8, `nsns-ha`, `nsns-oiii`, `nsns-sii` single lines); NSNS tiles inside its Moc.fits coverage are fetched from simg.de, stored as JPEG q85 and served at `/celestia-atlas-data/surveys/nsns`; only one survey download runs at a time

## 1.4.0.1
- fix(filesystem-preview): read the Bayer pattern from the file header, so OSC FITS/XISF frames offer the debayer option and render with their actual pattern

## 1.4.0.0
- feat(filesystem): add server-side preview/imageinfo endpoints

## 1.3.2.0
- Update Touch N Stars WebApp

## 1.3.1.0
- Update Touch N Stars WebApp

## 1.3.0.0
- Generated Celestia Atlas landscapes are now stored in the persistent N.I.N.A. user data; existing app-local landscapes are migrated without overwriting user content
- The `celestia-atlas-data` directory is now packaged and served

## 1.0.0.1

- Initial release
