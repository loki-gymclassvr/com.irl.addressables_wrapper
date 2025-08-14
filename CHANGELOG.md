# Changelog

## v1.4.0 - 2025-06-27
- 403 Error capturing and refreshing catalog more efficiently
- Remote Catalog Load with a lock and queue
- More bugsnags for specific download failures
- Fix for signed urls expiry tracking

## v1.4.0 - 2025-06-19
- Download queuing system
- Preload Manager to download/load sources before entering the scenes
- Improved HUD for downloads for cosmetics
- Update Unity Addressables package from 1.2.9 to 1.2.21

## v1.3.0 - 2025-05-29
- add bugsnags for addressable errors
- add signed urls timeout and auto refresh catalog unload previous
- add download and load player hud display for addressable content
- update addressable build using saved content state bin file

## v1.2.0 - 2025-05-16
- request signed urls catalog unloading unsigned catalog
- warmup internal transformer ids in a cache to serve asset load requests
- clearcache for failed downloads

## v1.1.0 - 2025-04-30
- addressable initialization for cdn and local bundles
- feature API call to download and load the target asset
- localhosting editor playmode feature for editor testing

## v1.0.0 - 2025-04-23

- runtime API wrapper for addressable load, unload, memory managment
- custom asset reference types with advanced addressable propertydrawers
- addressable grouping and labelling tool for custom configuration
- assetbundles build and upload to cloudflare cdn and localhosting
- addressable remote cdn download API