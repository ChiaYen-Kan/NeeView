// NeeView.Interop.cpp : DLL アプリケーション用にエクスポートされる関数を定義します。
//

#include "stdafx.h"

#include <iostream>
#include <shobjidl.h>
#include <shlguid.h>
#include <iterator>
#include <wrl/client.h>
#include <Windows.h>
#include <wincodec.h>

#include "NeeView.Interop.h"
#include "ImageCodecQuery.h"

static ImageCodecQuery* imageCodecQuery_ = nullptr;

bool __stdcall NVGetImageCodecInfo(unsigned int index, wchar_t* friendlyName, wchar_t* fileExtensions)
{
	if (imageCodecQuery_ == nullptr)
	{
		imageCodecQuery_ = new ImageCodecQuery();
		imageCodecQuery_->Initialize();
	}

	auto codecInfo = imageCodecQuery_->Get(index);
	if (codecInfo == nullptr)
	{
		return false;
	}

	wcscpy_s(friendlyName, CodecInfo::MaxLength, codecInfo->friendlyName);
	wcscpy_s(fileExtensions, CodecInfo::MaxLength, codecInfo->fileExtensions);
	return true;
}

void __stdcall NVCloseImageCodecInfo()
{
	if (imageCodecQuery_ != nullptr)
	{
		delete imageCodecQuery_;
		imageCodecQuery_ = nullptr;
	}

	_fpreset();
}

void __stdcall NVFpReset()
{
	_fpreset();
}


// from https://hirokio.jp/visualcpp/resolve_shortcut/
bool __stdcall NVGetFullPathFromShortcut(const wchar_t* shortcut, wchar_t* fullPath)
{
	HRESULT hres;
	
	*fullPath = 0;
	IShellLink* psl = NULL;

	hres = CoCreateInstance(CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER, IID_IShellLink, (void**)&psl);
	if (SUCCEEDED(hres))
	{
		IPersistFile* ppf = NULL;
		hres = psl->QueryInterface(IID_IPersistFile, (void**)&ppf);
		if (SUCCEEDED(hres))
		{
			hres = ppf->Load(shortcut, STGM_READ);
			if (SUCCEEDED(hres))
			{
				hres = psl->GetPath(fullPath, _MAX_PATH, NULL, 0);
				if (SUCCEEDED(hres))
				{
					ppf->Release();
					psl->Release();
					return true;
				}
			}
		}

		if (ppf != NULL)
		{
			ppf->Release();
		}
	}

	if (psl != NULL)
	{
		psl->Release();
	}

	return false;
}