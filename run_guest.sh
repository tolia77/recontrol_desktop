#!/bin/bash
export API_BASE_URL=http://192.168.122.1:3003
export WS_URL=ws://192.168.122.1:3003/cable
dotnet /mnt/recontrol/publish/ReControl.Desktop.dll
