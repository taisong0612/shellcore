using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class SkirmishOption
{
    public int creditLimit;
    public string mapDescription;
    public string entityID;
    public string sectorName;
}

// This class is used to control the window used to select and play Skirmish maps. Maps are defined by the class above
// Theoretically entityID and sectorName serve only as warp points making this a glorified warp interface
public class SkirmishMenu : GUIWindowScripts
{

    public List<SkirmishOption> options;
    [SerializeField]
    private Transform listContents;
    [SerializeField]
    private GameObject optionButton;
    [SerializeField]
    private InputField description;
    [SerializeField]
    private Text nameText;
    [SerializeField]
    private InputField creditLimitText;

    private SkirmishOption currentOption;

    public static SkirmishMenu instance;
    public void Start()
    {
        exitOnPlayerRange = true;
        instance = this;
    }

    public override void Activate()
    {
        foreach(Transform child in listContents)
        {
            Destroy(child.gameObject);
        }

        foreach(var option in options)
        {
            var curOpt = option;
            var button = Instantiate(optionButton, listContents).GetComponent<Button>();
            button.GetComponentInChildren<Text>().text = curOpt.sectorName;
            button.onClick.AddListener(() => 
            {
                currentOption = option;
                LoadMap();
                description.text = currentOption.mapDescription;
                nameText.text = currentOption.sectorName;
                creditLimitText.text = $"SHIP CREDIT LIMIT: {currentOption.creditLimit}";
            });
        }

        base.Activate();
    }

    public void LoadMap()
    {
        var sector = SectorManager.GetSectorByName(currentOption.sectorName);
        if(sector == null) return;
        List<Sector> sectors = new List<Sector>() {sector};
        GetComponentInChildren<MapMakerScript>().redraw(sectors, 1, sector.dimension);
    }

    public void ActivateCurrentOption()
    {
        Flag.FindEntityAndWarpPlayer(currentOption.sectorName, currentOption.entityID);
        CloseUI();
    }
}
