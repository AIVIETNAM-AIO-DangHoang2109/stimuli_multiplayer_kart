using System.Collections;
using System.Collections.Generic;
using UnityEngine;


///make attached object persistent across scene loads.
public class DontDestroyOnLoad : MonoBehaviour
{
    // Start is called before the first frame update
    void Awake()
    {
        DontDestroyOnLoad(gameObject);

    }

    // Update is called once per frame
    void Update()
    {

    }
}
