using UnityEngine;

enum MoveBoxState
{
	WaitingGo,
	Moving,
	WaitingBack,
	Returning
}

public class MoveBox : MonoBehaviour
{
	[SerializeField] private GameObject token;
	[SerializeField] private float moveSpeed = 3f;
	[SerializeField] private float waitTime = 1f;

	private Vector3 start;
	private Vector3 target;
	private float timer;

	private int state = (int)MoveBoxState.WaitingGo;

	void Start()
	{
		// Cubeの開始位置
		start = transform.position;

		// ゲーム開始時の「Tokenの実際のワールド座標」を記憶
		target = token.transform.position;

		token.SetActive(false);
	}

	void Update()
	{
		switch(state)
		{
			case (int)MoveBoxState.WaitingGo:
				timer += Time.deltaTime;
				if (timer >= waitTime)
				{
					state = (int)MoveBoxState.Moving;
					timer = 0f;
				}
				break;
			case (int)MoveBoxState.Moving:
				transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
				if (Vector3.Distance(transform.position, target) < 0.01f)
				{
					state = (int)MoveBoxState.WaitingBack;
					timer = 0f;
				}
				break;
			case (int)MoveBoxState.WaitingBack:
				timer += Time.deltaTime;
				if (timer >= waitTime)
				{
					state = (int)MoveBoxState.Returning;
					timer = 0f;
				}
				break;
			case (int)MoveBoxState.Returning:
				transform.position = Vector3.MoveTowards(transform.position, start, moveSpeed * Time.deltaTime);
				if (Vector3.Distance(transform.position, start) < 0.01f)
				{
					state = (int)MoveBoxState.WaitingGo;
					timer = 0f;
				}
				break;
		}
	}
}