@echo off
git submodule deinit -f GuidaSharedCode
git rm -f GuidaSharedCode
rmdir /S /Q GuidaSharedCode
rmdir /S /Q .git\modules\GuidaSharedCode
git submodule add https://github.com/GREAT-GUIDA/GuidaSharedCode GuidaSharedCode
git submodule update --remote GuidaSharedCode