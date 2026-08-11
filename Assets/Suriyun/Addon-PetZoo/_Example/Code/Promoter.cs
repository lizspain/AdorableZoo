using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Suriyun.PetZoo
{
	public class Promoter : MonoBehaviour
	{

		public void OpenURL(string url) {
			Application.OpenURL(url);
		}
	}
}