using System;
using System.Collections;
using System.IO;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private IEnumerator LocalizationSmoke()
        {
            for(int i=0;i<12;i++)yield return null;
            bool success=false;string language=L.Language;
            string key=AppLocalization.PreferenceKey;bool hadPreference=PlayerPrefs.HasKey(key);string oldPreference=PlayerPrefs.GetString(key,"");
            try
            {
                if(language!="en"&&language!="fr")throw new Exception("Unexpected QA language.");
                string expected=language=="fr"?"Enregistrer":"Save";
                if(!root.Query<Button>().ToList().Any(b=>b.text==expected))throw new Exception("Main UI not translated: "+language);
                if(catalog.Entries[1].Name!=(language=="fr"?"Plateforme":"Platform"))throw new Exception("Demo catalog initialized before language.");
                Select(snapshot.objects[0].id);ShowSettings(true);
                var choice=root.Q<DropdownField>("language-setting");
                if(choice==null||choice.label!=(language=="fr"?"Langue":"Language"))throw new Exception("Language setting missing.");
                string before=codec.Encode(snapshot);long revision=session.Revision;
                choice.index=AppLocalization.Languages.ToList().FindIndex(o=>o.Code==(language=="en"?"fr":"en"));
                if(PlayerPrefs.GetString(key,"")!=(language=="en"?"fr":"en"))throw new Exception("Language preference was not saved.");
                if(L.Language!=language||session.Revision!=revision||codec.Encode(snapshot)!=before)throw new Exception("Changing language modified an active map or mixed UI languages.");
                choice.index=AppLocalization.Languages.ToList().FindIndex(o=>o.Code==language);
                if(!File.Exists(Path.Combine(AppLocalization.Folder,"fr.json")))throw new Exception("Editable language files missing in player.");
                success=true;
            }
            catch(Exception ex){Debug.LogException(ex);}
            finally{if(hadPreference)PlayerPrefs.SetString(key,oldPreference);else PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();}
            for(int i=0;i<4;i++)yield return null;
            if(success)
            {
                yield return new WaitForEndOfFrame();var texture=ScreenCapture.CaptureScreenshotAsTexture();
                if(texture!=null){File.WriteAllBytes(Path.Combine(Application.dataPath,"..","localization-"+language+".png"),texture.EncodeToPNG());Destroy(texture);}
                Debug.Log("REMAP_LOCALIZATION_OK: "+language);
            }
            Application.Quit(success?0:1);
        }
    }
}
